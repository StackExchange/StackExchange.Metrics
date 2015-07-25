using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using BosunReporter.Infrastructure;
using Jil;

namespace BosunReporter
{
    public partial class MetricsCollector
    {
        private class RootMetricInfo
        {
            public Type Type { get; set; }
            public string Unit { get; set; }
        }

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        
        private readonly object _metricsLock = new object();
        // all of the first-class names which have been claimed (excluding suffixes in aggregate gauges)
        private readonly Dictionary<string, RootMetricInfo> _rootNameToInfo = new Dictionary<string, RootMetricInfo>();
        // this dictionary is to avoid duplicate metrics
        private Dictionary<string, BosunMetric> _rootNameAndTagsToMetric = new Dictionary<string, BosunMetric>();
        // All of the names which have been claimed, including the metrics which may have multiple suffixes, mapped to their root metric name.
        // This is to prevent suffix collisions with other metrics.
        private readonly Dictionary<string, string> _nameAndSuffixToRootName = new Dictionary<string, string>();
        
        private List<string> _pendingMetrics;
        private readonly object _pendingLock = new object();
        private readonly object _flushingLock = new object();
        private int _isFlushing = 0; // int instead of a bool so in can be used with Interlocked.CompareExchange
        private int _skipFlushes = 0;
        private readonly Timer _flushTimer;
        private readonly Timer _reportingTimer;
        private readonly Timer _metaDataTimer;

        private static readonly AsyncCallback s_asyncNoopCallback = AsyncNoopCallback;

        internal Dictionary<Type, List<BosunTag>> TagsByTypeCache = new Dictionary<Type, List<BosunTag>>();

        // options
        public string MetricsNamePrefix { get; }
        public Uri BosunUrl { get; set; }
        public Func<Uri> GetBosunUrl { get; set; }
        public int MaxQueueLength { get; set; }
        public int BatchSize { get; set; }
        public bool ThrowOnPostFail { get; set; }
        public bool ThrowOnQueueFull { get; set; }
        public int ReportingInterval { get; }
        public Func<string, string> PropertyToTagName { get; }
        public TagValueConverterDelegate TagValueConverter { get; }
        public ReadOnlyDictionary<string, string> DefaultTags { get; private set; }

        public bool ShutdownCalled { get; private set; }

        // insights
        /// <summary>
        /// The number of metrics (individual data points) which are currently queued for sending to Bosun.
        /// </summary>
        public int PendingMetricsCount => _pendingMetrics?.Count ?? 0;
        public bool HasPendingMetrics => PendingMetricsCount > 0;
        /// <summary>
        /// Total number of data points successfully sent fo Bosun.
        /// </summary>
        public long TotalMetricsPosted { get; private set; }
        /// <summary>
        /// The number of times an HTTP POST to Bosun's /api/put endpoint has succeeded.
        /// </summary>
        public int PostSuccessCount { get; private set; }
        /// <summary>
        /// The number of times an HTTP POST to Bosun's /api/put endpoint has failed.
        /// </summary>
        public int PostFailCount { get; private set; }

        public AfterSerializationInfo LastSerializationInfo { get; private set; }
        public AfterPostInfo LastPostInfo { get; private set; }

        public event Action<Exception> OnBackgroundException;
        public bool HasExceptionHandler => OnBackgroundException != null && OnBackgroundException.GetInvocationList().Length != 0;

        public event Action BeforeSerialization;
        public event Action<AfterSerializationInfo> AfterSerialization;
        public event Action<AfterPostInfo> AfterPost;

        public IEnumerable<BosunMetric> Metrics => _rootNameAndTagsToMetric.Values.AsEnumerable();

        public MetricsCollector(BosunOptions options)
        {
            MetricsNamePrefix = options.MetricsNamePrefix ?? "";
            if (MetricsNamePrefix != "" && !BosunValidation.IsValidMetricName(MetricsNamePrefix))
                throw new Exception("\"" + MetricsNamePrefix + "\" is not a valid metric name prefix.");

            GetBosunUrl = options.GetBosunUrl;
            BosunUrl = GetBosunUrl == null ? options.BosunUrl : GetBosunUrl();
            MaxQueueLength = options.MaxQueueLength;
            BatchSize = options.BatchSize;
            ThrowOnPostFail = options.ThrowOnPostFail;
            ThrowOnQueueFull = options.ThrowOnQueueFull;
            ReportingInterval = options.ReportingInterval;
            PropertyToTagName = options.PropertyToTagName;
            TagValueConverter = options.TagValueConverter;
            DefaultTags = ValidateDefaultTags(options.DefaultTags);

            // start continuous queue-flushing
            _flushTimer = new Timer(Flush, true, 1000, 1000);

            // start reporting timer
            var interval = TimeSpan.FromSeconds(ReportingInterval);
            _reportingTimer = new Timer(Snapshot, true, interval, interval);

            // metadata timer - wait 30 seconds to start (so there is some time for metrics to be delcared)
            if (options.MetaDataReportingInterval > 0)
                _metaDataTimer = new Timer(PostMetaData, true, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(options.MetaDataReportingInterval));
        }

        public bool TryGetMetricInfo(string name, out Type type, out string unit)
        {
            return TryGetMetricWithoutPrefixInfo(MetricsNamePrefix + name, out type, out unit);
        }

        public bool TryGetMetricWithoutPrefixInfo(string name, out Type type, out string unit)
        {
            RootMetricInfo rmi;
            if (_rootNameToInfo.TryGetValue(name, out rmi))
            {
                type = rmi.Type;
                unit = rmi.Unit;
                return true;
            }

            type = null;
            unit = null;
            return false;
        }

        private ReadOnlyDictionary<string, string> ValidateDefaultTags(Dictionary<string, string> tags)
        {
            var defaultTags = tags == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(tags);

            foreach (var key in defaultTags.Keys.ToArray())
            {
                if (!BosunValidation.IsValidTagName(key))
                    throw new Exception($"\"{key}\" is not a valid Bosun tag name.");

                if (TagValueConverter != null)
                    defaultTags[key] = TagValueConverter(key, defaultTags[key]);

                if (!BosunValidation.IsValidTagValue(defaultTags[key]))
                    throw new Exception($"\"{defaultTags[key]}\" is not a valid Bosun tag value.");
            }

            return new ReadOnlyDictionary<string, string>(defaultTags);
        }

        public void BindMetric(string name, string unit, Type type)
        {
            BindMetricWithoutPrefix(MetricsNamePrefix + name, unit, type);
        }

        public void BindMetricWithoutPrefix(string name, string unit, Type type)
        {
            lock (_metricsLock)
            {
                RootMetricInfo rmi;
                if (_rootNameToInfo.TryGetValue(name, out rmi))
                {
                    if (rmi.Type != type)
                    {
                        throw new Exception($"Cannot bind metric name \"{name}\" to Type {type.FullName}. It has already been bound to {rmi.Type.FullName}");
                    }

                    if (rmi.Unit != unit)
                    {
                        throw new Exception($"Cannot bind metric name \"{name}\" to unit \"{unit}\". It has already been bound to \"{rmi.Unit}\"");
                    }

                    return;
                }

                if (!type.IsSubclassOf(typeof (BosunMetric)))
                {
                    throw new Exception($"Cannot bind metric \"{name}\" to Type {type.FullName}. It does not inherit from BosunMetric.");
                }

                _rootNameToInfo[name] = new RootMetricInfo { Type = type, Unit = unit };
            }
        }

        /// <summary>
        /// Creates a metric and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The MetricsNamePrefix will be prepended to the metric name.
        /// </summary>
        public T CreateMetric<T>(string name, string unit, string description, Func<T> metricFactory) where T : BosunMetric
        {
            return GetMetricInternal(name, true, unit, description, metricFactory(), true);
        }

        public T CreateMetric<T>(string name, string unit, string description, T metric = null) where T : BosunMetric
        {
            return GetMetricInternal(name, true, unit, description, metric, true);
        }

        /// <summary>
        /// Creates a metric and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The MetricsNamePrefix will NOT be prepended to the metric name.
        /// </summary>
        public T CreateMetricWithoutPrefix<T>(string name, string unit, string description, Func<T> metricFactory) where T : BosunMetric
        {
            return GetMetricInternal(name, false, unit, description, metricFactory(), true);
        }

        public T CreateMetricWithoutPrefix<T>(string name, string unit, string description, T metric = null) where T : BosunMetric
        {
            return GetMetricInternal(name, false, unit, description, metric, true);
        }

        /// <summary>
        /// Creates a metric and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is returned.
        /// The MetricsNamePrefix will be prepended to the metric name.
        /// </summary>
        public T GetMetric<T>(string name, string unit, string description, Func<T> metricFactory) where T : BosunMetric
        {
            return GetMetricInternal(name, true, unit, description, metricFactory(), false);
        }

        public T GetMetric<T>(string name, string unit, string description, T metric = null) where T : BosunMetric
        {
            return GetMetricInternal(name, true, unit, description, metric, false);
        }

        /// <summary>
        /// Creates a metric and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is returned.
        /// The MetricsNamePrefix will NOT be prepended to the metric name.
        /// </summary>
        public T GetMetricWithoutPrefix<T>(string name, string unit, string description, Func<T> metricFactory) where T : BosunMetric
        {
            return GetMetricInternal(name, false, unit, description, metricFactory(), false);
        }

        public T GetMetricWithoutPrefix<T>(string name, string unit, string description, T metric = null) where T : BosunMetric
        {
            return GetMetricInternal(name, false, unit, description, metric, false);
        }

        private T GetMetricInternal<T>(string name, bool addPrefix, string unit, string description, T metric, bool mustBeNew) where T : BosunMetric
        {
            if (addPrefix)
                name = MetricsNamePrefix + name;

            var metricType = typeof (T);
            if (metric == null)
            {
                // if the type has a constructor without params, then create an instance
                var constructor = metricType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (constructor == null)
                    throw new ArgumentNullException(nameof(metric), metricType.FullName + " has no public default constructor. Therefore the metric parameter cannot be null.");
                metric = (T)constructor.Invoke(new object[0]);
            }
            metric.Collector = this;

            metric.Name = name;
            metric.Description = description;
            metric.Unit = unit;

            lock (_metricsLock)
            {
                RootMetricInfo rmi;
                if (_rootNameToInfo.TryGetValue(name, out rmi))
                {
                    if (rmi.Type != metricType)
                    {
                        throw new Exception(
                            $"Attempted to create metric name \"{name}\" with Type {metricType.FullName}. This metric name has already been bound to Type {rmi.Type.FullName}.");
                    }

                    if (rmi.Unit != unit)
                    {
                        throw new Exception(
                            $"Cannot bind metric name \"{name}\" to unit \"{unit}\". It has already been bound to \"{rmi.Unit}\"");
                    }
                }
                else if (_nameAndSuffixToRootName.ContainsKey(name))
                {
                    throw new Exception(
                        $"Attempted to create metric name \"{name}\" with Type {metricType.FullName}. " +
                        $"This metric name is already in use as a suffix of Type {_rootNameToInfo[_nameAndSuffixToRootName[name]].Type.FullName}.");
                }

                // claim all suffixes. Do this in two passes (check then add) so we don't end up in an inconsistent state.
                foreach (var s in metric.Suffixes)
                {
                    var ns = name + s;
                        
                    // verify this is a valid metric name at all (it should be, since both parts are pre-validated, but just in case).
                    if (!BosunValidation.IsValidMetricName(ns))
                        throw new Exception($"\"{ns}\" is not a valid metric name");

                    if (_nameAndSuffixToRootName.ContainsKey(ns) && _nameAndSuffixToRootName[ns] != name)
                    {
                        throw new Exception(
                            $"Attempted to create metric name \"{ns}\" with Type {metricType.FullName}. " +
                            $"This metric name is already in use as a suffix of Type {_rootNameToInfo[_nameAndSuffixToRootName[ns]].Type.FullName}.");
                    }
                }

                foreach (var s in metric.Suffixes)
                {
                    _nameAndSuffixToRootName[name + s] = name;
                }

                // claim the root type
                _rootNameToInfo[name] = new RootMetricInfo { Type = metricType, Unit = unit };

                // see if this metric name and tag combination already exists
                var key = metric.MetricKey;
                if (_rootNameAndTagsToMetric.ContainsKey(key))
                {
                    if (mustBeNew)
                        throw new Exception($"Attempted to create duplicate metric with name \"{name}\" and tags {metric.TagsJson}.");

                    return (T) _rootNameAndTagsToMetric[key];
                }

                // metric doesn't exist yet.
                _rootNameAndTagsToMetric[key] = metric;
                metric.IsAttached = true;

                if (metric.SerializeInitialValue)
                    EnqueueMetrics(metric.SerializeInternal(GetUnixTimestamp()));

                return metric;
            }
        }

        /// <summary>
        /// This method should only be called on application shutdown, and should not be called more than once.
        /// When called, it runs one final metric snapshot and makes a single attempt to flush all metrics in the queue.
        /// </summary>
        public void Shutdown()
        {
            Debug.WriteLine("BosunReporter: Shutting down MetricsCollector.");
            ShutdownCalled = true;
            _reportingTimer.Dispose();
            _flushTimer.Dispose();
            _metaDataTimer.Dispose();
            Snapshot(false);
            Flush(false);
        }

        public void UpdateDefaultTags(Dictionary<string, string> defaultTags)
        {
            // validate
            var validated = ValidateDefaultTags(defaultTags);

            // don't want any new metrics to be created while we're figuring things out
            lock (_metricsLock)
            {
                // first, check if anything actually changed
                if (AreIdenticalTags(DefaultTags, validated))
                {
                    Debug.WriteLine("Not updating default tags. The new defaults are the same as the previous defaults.");
                    return;
                }

                // there are differences, now make sure we can apply the new defaults without collisions
                var rootNameAndTagsToMetric = new Dictionary<string, BosunMetric>();
                var tagsByTypeCache = new Dictionary<Type, List<BosunTag>>();
                var tagsJsonByKey = new Dictionary<string, string>();
                foreach (var m in Metrics)
                {
                    var tagsJson = m.GetTagsJson(validated, TagValueConverter, tagsByTypeCache);
                    var key = m.GetMetricKey(tagsJson);

                    if (rootNameAndTagsToMetric.ContainsKey(key))
                    {
                        throw new InvalidOperationException("Cannot update default tags. Doing so would cause collisions.");
                    }

                    rootNameAndTagsToMetric.Add(key, m);
                    tagsJsonByKey.Add(key, tagsJson);
                }

#if DEBUG
                Debug.WriteLine("Updating default tags:");
                foreach (var kvp in validated)
                {
                    Debug.WriteLine("  " + kvp.Key + ": " + kvp.Value);
                }
#endif

                // looks like we can successfully swap in the new default tags
                foreach (var kvp in rootNameAndTagsToMetric)
                {
                    var key = kvp.Key;
                    var m = kvp.Value;
                    m.SwapTagsJson(tagsJsonByKey[key]);
                }

                TagsByTypeCache = tagsByTypeCache;
                _rootNameAndTagsToMetric = rootNameAndTagsToMetric;
                DefaultTags = validated;
            }
        }

        private static bool AreIdenticalTags(ReadOnlyDictionary<string, string> a, ReadOnlyDictionary<string, string> b)
        {
            foreach (var kvp in a)
            {
                if (!b.ContainsKey(kvp.Key) || b[kvp.Key] != kvp.Value)
                    return false;
            }

            foreach (var kvp in b)
            {
                if (!a.ContainsKey(kvp.Key) || a[kvp.Key] != kvp.Value)
                    return false;
            }

            return true;
        }

        private void Snapshot(object isCalledFromTimer)
        {
            if ((bool)isCalledFromTimer && ShutdownCalled) // don't perform timer actions if we're shutting down
                return;

            if (GetBosunUrl != null)
                BosunUrl = GetBosunUrl();


            try
            {
                if (BeforeSerialization != null && BeforeSerialization.GetInvocationList().Length != 0)
                    BeforeSerialization();

                var info = new AfterSerializationInfo();
                var sw = new StopwatchStruct();
                sw.Start();
                var list = GetSerializedMetrics();
                sw.Stop();
                
                EnqueueMetrics(list);

                info.Count = list.Count;
                info.MillisecondsDuration = sw.GetElapsedMilliseconds();

                LastSerializationInfo = info;
                AfterSerialization?.Invoke(info);
            }
            catch (Exception e)
            {
                if (HasExceptionHandler)
                {
                    OnBackgroundException(e);
                    return;
                }

                throw;
            }
        }

        private void Flush(object isCalledFromTimer)
        {
            if ((bool)isCalledFromTimer && ShutdownCalled) // don't perform timer actions if we're shutting down
                return;

            // prevent calls to Flush from stacking up - skip this check if we're in draining mode
            if (Interlocked.CompareExchange(ref _isFlushing, 1, 0) != 0 && !ShutdownCalled)
            {
                Debug.WriteLine("BosunReporter: Flush already in progress (skipping).");
                return;
            }

            lock (_flushingLock)
            {
                try
                {
                    if (!ShutdownCalled && _skipFlushes > 0)
                    {
                        _skipFlushes--;
                        return;
                    }

                    while (_pendingMetrics != null && _pendingMetrics.Count > 0)
                    {
                        FlushBatch();
                    }
                }
                catch (BosunPostException ex)
                {
                    // there was a problem flushing - back off for the next five seconds (Bosun may simply be restarting)
                    _skipFlushes = 4;
                    if (ThrowOnPostFail)
                    {
                        if (HasExceptionHandler)
                            OnBackgroundException(ex);
                        else
                            throw;
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isFlushing, 0);
                }
            }
        }

        private void FlushBatch()
        {
            var batch = DequeueMetricsBatch();
            if (batch.Count == 0)
                return;

            Debug.WriteLine("BosunReporter: Flushing metrics batch. Size: " + batch.Count);

            var info = new AfterPostInfo();
            var timer = new StopwatchStruct();
            try
            {
                timer.Start();
                PostToBosun("/api/put", true, sw => WriteJsonArrayBody(sw, batch));
                timer.Stop();

                PostSuccessCount++;
                TotalMetricsPosted += batch.Count;
            }
            catch (Exception ex)
            {
                timer.Stop();
                // posting to Bosun failed, so put the batch back in the queue to try again later
                Debug.WriteLine("BosunReporter: Posting to the Bosun API failed. Pushing metrics back onto the queue.");
                PostFailCount++;
                info.Exception = ex;
                EnqueueMetrics(batch);
                throw;
            }
            finally
            {
                info.Count = batch.Count;
                info.MillisecondsDuration = timer.GetElapsedMilliseconds();
                LastPostInfo = info;

                // Use BeginInvoke here to invoke the event listeners asynchronously.
                // We're inside a lock, so calling the listeners synchronously would put us at risk of a deadlock.
                AfterPost?.BeginInvoke(info, s_asyncNoopCallback, null);
            }
        }

        private static void AsyncNoopCallback(IAsyncResult result) { }

        private static void WriteJsonArrayBody(StreamWriter sw, List<string> batch)
        {
            sw.Write("[");

            var first = true;
            foreach (var o in batch)
            {
                if (first)
                    first = false;
                else
                    sw.Write(",");

                sw.Write(o);
            }

            sw.Write("]");
        }

        private delegate void ApiPostWriter(StreamWriter sw);
        private void PostToBosun(string path, bool gzip, ApiPostWriter postWriter)
        {
            var url = BosunUrl;
            if (url == null)
            {
                Debug.WriteLine("BosunReporter: BosunUrl is null. Dropping data.");
                return;
            }

            url = new Uri(url, path);

            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            if (gzip)
                request.Headers["Content-Encoding"] = "gzip";

            // support for http://username:password@domain.com, by default this does not work
            var userInfo = url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
            if (!string.IsNullOrEmpty(userInfo))
            {
                var auth = $"Basic {0}" + Convert.ToBase64String(Encoding.Default.GetBytes(userInfo));
                request.Headers["Authorization"] = auth;
            }

            try
            {
                using (var stream = request.GetRequestStream())
                {
                    if (gzip)
                    {
                        using (var gzipStream = new GZipStream(stream, CompressionMode.Compress))
                        using (var sw = new StreamWriter(gzipStream, new UTF8Encoding(false)))
                        {
                            postWriter(sw);
                        }
                    }
                    else
                    {
                        using (var sw = new StreamWriter(stream, new UTF8Encoding(false)))
                        {
                            postWriter(sw);
                        }
                    }
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
                {
                    var ex = new BosunQueueFullException();

                    if (HasExceptionHandler)
                        OnBackgroundException(ex);
                    else
                        throw ex;
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

        internal static string GetUnixTimestamp()
        {
            return GetUnixTimestamp(DateTime.UtcNow);
        }
        internal static string GetUnixTimestamp(DateTime time)
        {
            return ((long)(time - UnixEpoch).TotalMilliseconds).ToString("D");
        }

        private List<string> GetSerializedMetrics()
        {
            var unixTimestamp = GetUnixTimestamp();
            lock (_metricsLock)
            {
                return Metrics.AsParallel().Select(m => m.SerializeInternal(unixTimestamp)).SelectMany(s => s).ToList();
            }
        }

        private void PostMetaData(object _)
        {
            if (ShutdownCalled) // don't report any more meta data if we're shutting down
                return;

            if (BosunUrl == null)
            {
                Debug.WriteLine("BosunReporter: BosunUrl is null. Not sending metadata.");
                return;
            }

            try
            {
                Debug.WriteLine("BosunReporter: Gathering metadata.");
                var metaJson = GatherMetaData();
                Debug.WriteLine("BosunReporter: Sending metadata.");
                PostToBosun("/api/metadata/put", false, sw => sw.Write(metaJson));
            }
            catch (BosunPostException)
            {
                if (ThrowOnPostFail)
                    throw;
            }
        }

        private string GatherMetaData()
        {
            var metaList = new List<MetaData>();
            var json = new StringBuilder();

            lock (_metricsLock)
            {
                foreach (var metric in Metrics)
                {
                    if (metric == null)
                        continue;

                    foreach (var meta in metric.GetMetaData())
                    {
                        json.Append(",{\"Metric\":\"" + meta.Metric +
                            "\",\"Name\":\"" + meta.Name +
                            "\",\"Value\":" + JSON.Serialize(meta.Value) +
                            (meta.Tags == null ? "" : ",\"Tags\":" + meta.Tags) +
                            "}\n");
                    }
                }
            }

            if (json.Length == 0)
                return "[]";

            json[0] = '['; // replace the first comma with an open bracket
            json.Append(']');

            return json.ToString();
        }
    }

    public class AfterSerializationInfo
    {
        public int Count { get; internal set; }
        public double MillisecondsDuration { get; internal set; }
        public DateTime StartTime { get; }

        public AfterSerializationInfo()
        {
            StartTime = DateTime.UtcNow;
        }
    }

    public class AfterPostInfo
    {
        public int Count { get; internal set; }
        public double MillisecondsDuration { get; internal set; }
        public bool Successful => Exception == null;
        public Exception Exception { get; internal set; }
        public DateTime StartTime { get; }

        public AfterPostInfo()
        {
            StartTime = DateTime.UtcNow;
        }
    }
}
