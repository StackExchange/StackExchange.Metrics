
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

namespace BosunReporter
{
    public class BosunReporter
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        // all of the first-class names which have been claimed (excluding suffixes in aggregate gauges)
        private readonly Dictionary<string, Type> _rootNameToType = new Dictionary<string, Type>();
        // this dictionary is to avoid duplicate metrics
        private readonly Dictionary<string, BosunMetric> _rootNameAndTagsToMetric = new Dictionary<string, BosunMetric>();
        // All of the name which have been claimed, including the metrics which may have multiple suffixes, mapped to their root metric name.
        // This is to prevent suffix collisions with other metrics.
        private readonly Dictionary<string, string> _nameAndSuffixToRootName = new Dictionary<string, string>();

        private List<string> _pendingMetrics;
        private readonly object _pendingLock = new object();
        private readonly object _flushingLock = new object();
        private int _isFlushing = 0; // int instead of a bool so in can be used with Interlocked.CompareExchange
        private int _skipFlushes = 0;
        private Timer _flushTimer;
        private Timer _reportingTimer;

        // options
        public readonly string MetricsNamePrefix;
        public Uri BosunUrl;
        public Func<Uri> GetBosunUrl;
        public int MaxQueueLength;
        public int BatchSize;
        public bool ThrowOnPostFail;
        public bool ThrowOnQueueFull;
        public readonly int ReportingInterval;

        public IEnumerable<BosunMetric> Metrics
        {
            get { return _rootNameAndTagsToMetric.Values.AsEnumerable(); }
        }

        public BosunReporter(BosunReporterOptions options)
        {
            MetricsNamePrefix = options.MetricsNamePrefix ?? "";
            if (MetricsNamePrefix != "" && !Validation.IsValidMetricName(MetricsNamePrefix))
                throw new Exception("\"" + MetricsNamePrefix + "\" is not a valid metric name prefix.");

            BosunUrl = options.BosunUrl;
            GetBosunUrl = options.GetBosunUrl;
            MaxQueueLength = options.MaxQueueLength;
            BatchSize = options.BatchSize;
            ThrowOnPostFail = options.ThrowOnPostFail;
            ThrowOnQueueFull = options.ThrowOnQueueFull;
            ReportingInterval = options.ReportingInterval;

            // start continuous queue-flushing
            _flushTimer = new Timer(Flush, null, 1000, 1000);

            // start reporting timer
            var interval = TimeSpan.FromSeconds(ReportingInterval);
            _reportingTimer = new Timer(Snapshot, null, interval, interval);
        }

        public T GetMetric<T>(string name, T metric = null) where T : BosunMetric
        {
            var metricType = typeof (T);
            if (metric == null)
            {
                // if the type has a constructor without params, then create an instance
                var constructor = metricType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (constructor == null)
                    throw new ArgumentNullException("metric", metricType.FullName + " has no public default constructor. Therefore the metric parameter cannot be null.");
                metric = (T)constructor.Invoke(new object[0]);
            }

            name = MetricsNamePrefix + name;
            lock (_rootNameToType)
            {
                if (_nameAndSuffixToRootName.ContainsKey(name) && (!_rootNameToType.ContainsKey(name) || _rootNameToType[name] != metricType))
                {
                    if (_rootNameToType.ContainsKey(name))
                    {
                        throw new Exception(
                            String.Format(
                                "Attempted to create metric name \"{0}\" with Type {1}. This metric name has already been assigned to Type {2}.",
                                name, metricType.FullName, _rootNameToType[name].FullName));
                    }

                    throw new Exception(
                        String.Format(
                            "Attempted to create metric name \"{0}\" with Type {1}. This metric name is already in use as a suffix of Type {2}.",
                            name, metricType.FullName, _rootNameToType[_nameAndSuffixToRootName[name]].FullName));
                }

                // claim all suffixes. Do this in two passes (check then add) so we don't end up in an inconsistent state.
                foreach (var s in metric.Suffixes)
                {
                    var ns = name + s;
                        
                    // verify this is a valid metric name at all (it should be, since both parts are pre-validated, but just in case).
                    if (!Validation.IsValidMetricName(ns))
                        throw new Exception(String.Format("\"{0}\" is not a valid metric name", ns));

                    if (_nameAndSuffixToRootName.ContainsKey(ns) && _nameAndSuffixToRootName[ns] != name)
                    {
                        throw new Exception(
                            String.Format(
                                "Attempted to create metric name \"{0}\" with Type {1}. This metric name is already in use as a suffix of Type {2}.",
                                ns, metricType.FullName, _rootNameToType[_nameAndSuffixToRootName[ns]].FullName));
                    }
                }

                foreach (var s in metric.Suffixes)
                {
                    _nameAndSuffixToRootName[name + s] = name;
                }

                // claim the root type
                _rootNameToType[name] = metricType;

                // see if this metric name and tag combination already exists
                var nameAndTags = name + metric.SerializedTags;
                if (_rootNameAndTagsToMetric.ContainsKey(nameAndTags))
                    return (T) _rootNameAndTagsToMetric[nameAndTags];

                // metric doesn't exist yet.
                metric.Name = name;
                metric.BosunReporter = this;
                _rootNameAndTagsToMetric[nameAndTags] = metric;
                return metric;
            }
        }

        private void Snapshot(object _)
        {
            Debug.WriteLine("BosunReporter: Running metrics snapshot.");
            EnqueueMetrics(GetSerializedMetrics());
        }

        private void Flush(object _)
        {
            // prevent being called simultaneously
            if (Interlocked.CompareExchange(ref _isFlushing, 1, 0) != 0)
            {
                Debug.WriteLine("BosunReporter: Flush already in progress (skipping).");
                return;
            }

            try
            {
                if (_skipFlushes > 0)
                {
                    _skipFlushes--;
                    return;
                }

                while (_pendingMetrics != null && _pendingMetrics.Count > 0)
                {
                    FlushBatch();
                }
            }
            catch (BosunPostException)
            {
                // there was a problem flushing - back off for the next five seconds (Bosun may simply be restarting)
                _skipFlushes = 4;
                if (ThrowOnPostFail)
                    throw;
            }
            finally
            {
                Interlocked.Exchange(ref _isFlushing, 0);
            }
        }

        private void FlushBatch()
        {
            var batch = DequeueMetricsBatch();
            if (batch.Count == 0)
                return;

            Debug.WriteLine("BosunReporter: Flushing metrics batch. Size: " + batch.Count);

            var body = '[' + String.Join(",", batch) + ']';

            try
            {
                PostToBosun(body);
            }
            catch (Exception)
            {
                // posting to Bosun failed, so put the batch back in the queue to try again later
                Debug.WriteLine("BosunReporter: Posting to the Bosun API failed. Pushing metrics back onto the queue.");
                EnqueueMetrics(batch);
                throw;
            }
        }

        private void PostToBosun(string body)
        {
            Uri url = GetBosunUrl != null ? GetBosunUrl() : BosunUrl;
            if (url == null)
                return;

            url = new Uri(url, "/api/put");

            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers["Content-Encoding"] = "gzip";

            try
            {
                using (var stream = request.GetRequestStream())
                using (var gzip = new GZipStream(stream, CompressionMode.Compress))
                using (var sw = new StreamWriter(gzip, new UTF8Encoding(false)))
                {
                    sw.Write(body);
                }

                request.GetResponse().Close();
            }
            catch (WebException e)
            {
                using (var response = (HttpWebResponse)e.Response)
                {
                    if (response == null)
                    {
                        throw new BosunPostException(e);
                    }

                    using (Stream data = response.GetResponseStream())
                    using (var reader = new StreamReader(data))
                    {
                        string text = reader.ReadToEnd();
                        throw new BosunPostException(response.StatusCode, text, e);
                    }
                }
            }
        }

        private void EnqueueMetrics(IEnumerable<string> metrics)
        {
            lock (_pendingLock)
            {
                if (_pendingMetrics == null || _pendingMetrics.Count == 0)
                {
                    _pendingMetrics = metrics.Take(MaxQueueLength).ToList();
                }
                else
                {
                    _pendingMetrics.AddRange(metrics.Take(MaxQueueLength - _pendingMetrics.Count));
                }

                if (ThrowOnQueueFull && _pendingMetrics.Count == MaxQueueLength)
                    throw new BosunQueueFullException();
            }
        }

        private List<string> DequeueMetricsBatch()
        {
            lock (_pendingLock)
            {
                List<string> batch;
                if (_pendingMetrics == null)
                    return new List<string>();

                if (_pendingMetrics.Count <= BatchSize)
                {
                    batch = _pendingMetrics;
                    _pendingMetrics = null;
                    return batch;
                }

                // todo: this is not a great way to do this perf-wise
                batch = _pendingMetrics.GetRange(0, BatchSize);
                _pendingMetrics.RemoveRange(0, BatchSize);
                return batch;
            }
        }

        private IEnumerable<string> GetSerializedMetrics()
        {
            var unixTimestamp = ((long)(DateTime.UtcNow - UnixEpoch).TotalSeconds).ToString("D");
            return Metrics.Select(m => m.Serialize(unixTimestamp)).SelectMany(s => s);
        }
    }
}
