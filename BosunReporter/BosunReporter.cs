
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

namespace BosunReporter
{
    public class BosunReporter
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        private readonly Dictionary<string, Type> _nameToType = new Dictionary<string, Type>();
        private readonly Dictionary<string, BosunMetric> _nameAndTagsToMetric = new Dictionary<string, BosunMetric>();

        private List<string> _pendingMetrics;
        private readonly object _pendingLock = new object();
        private readonly object _flushingLock = new object();

        // options
        public readonly string MetricsNamePrefix;
        public Uri BosunUrl;
        public Func<Uri> GetBosunUrl;
        public int MaxQueueLength;
        public int BatchSize;
        public bool ThrowOnPostFail;
        public bool ThrowOnQueueFull;

        public IEnumerable<BosunMetric> Metrics
        {
            get { return _nameAndTagsToMetric.Values.AsEnumerable(); }
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
            lock (_nameToType)
            {
                // make sure there's not already another type assigned to this metric
                if (_nameToType.ContainsKey(name))
                {
                    if (_nameToType[name] != metricType)
                    {
                        throw new Exception(
                            String.Format(
                                "Attempted to create metric name \"{0}\" with Type {1}. This metric name has already been assigned to Type {2}.",
                                name, metricType.FullName, _nameToType[name].FullName));
                    }
                }
                else
                {
                    _nameToType[name] = metricType;
                }

                // see if this metric name and tag combination already exists
                var nameAndTags = name + metric.SerializeTags();
                if (_nameAndTagsToMetric.ContainsKey(nameAndTags))
                    return (T) _nameAndTagsToMetric[nameAndTags];

                // metric doesn't exist yet.
                metric.Name = name;
                metric.BosunReporter = this;
                _nameAndTagsToMetric[nameAndTags] = metric;
                return metric;
            }
        }

        public void SnapshotAndFlush()
        {
            EnqueueMetrics(GetSerializedMetrics());
            FlushPending();
        }

        public bool FlushPending()
        {
            lock (_flushingLock)
            {
                int batches;
                lock (_pendingLock)
                {
                    if (_pendingMetrics == null || _pendingMetrics.Count == 0)
                        return true;

                    // don't try to batch more than were in the pending list at the start (avoid infinite flushing loop)
                    batches = (int) Math.Ceiling((double) _pendingMetrics.Count/BatchSize);
                }

                for (var i = 0; i < batches; i++)
                {
                    if (FlushBatch() == 0)
                        return false;
                }

                return true;
            }
        }

        public int FlushBatch()
        {
            var batch = DequeueMetricsBatch();
            if (batch.Count == 0)
                return 0;

            var body = '[' + String.Join(",", batch) + ']';

            try
            {
                PostToBosun(body);
            }
            catch (Exception ex)
            {
                // posting to Bosun failed, so put the batch back in the queue to try again later
                EnqueueMetrics(batch);

                if (ThrowOnPostFail)
                    throw;

                // if queue is full, we might be losing data... now is the time to throw an exception
                if (ThrowOnQueueFull && _pendingMetrics != null && _pendingMetrics.Count == MaxQueueLength)
                    throw new Exception("Bosun metric queue is full. Metric data is likely being lost due to repeated failures in posting to the Bosun API.", ex);

                return 0;
            }

            return batch.Count;
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

            using (var stream = request.GetRequestStream())
            using (var gzip = new GZipStream(stream, CompressionMode.Compress))
            using (var sw = new StreamWriter(gzip, new UTF8Encoding(false)))
            {
                sw.Write(body);
            }

            try
            {
                request.GetResponse();
            }
            catch (WebException e)
            {
                using (var response = (HttpWebResponse)e.Response)
                {
                    using (Stream data = response.GetResponseStream())
                    using (var reader = new StreamReader(data))
                    {
                        string text = reader.ReadToEnd();
                        var ex = new Exception("Posting to the Bosun API failed with status code " + response.StatusCode, e);
                        ex.Data["ResponseBody"] = text;
                        throw ex;
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
                    _pendingMetrics = metrics.ToList();
                }
                else
                {
                    _pendingMetrics.AddRange(metrics.Take(MaxQueueLength - _pendingMetrics.Count));
                }
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
