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
using System.Threading.Tasks;
using BosunReporter.Infrastructure;

namespace BosunReporter
{
    /// <summary>
    /// The primary class for BosunReporter. Use this class to create metrics for reporting to Bosun.
    /// </summary>
    public partial class MetricsCollector
    {
        class RootMetricInfo
        {
            public Type Type { get; set; }
            public string Unit { get; set; }
        }

        static readonly DateTime s_unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        static readonly MetricKeyComparer s_metricKeyComparer = new MetricKeyComparer();

        readonly object _metricsLock = new object();
        // all of the first-class names which have been claimed (excluding suffixes in aggregate gauges)
        readonly Dictionary<string, RootMetricInfo> _rootNameToInfo = new Dictionary<string, RootMetricInfo>();
        // this dictionary is to avoid duplicate metrics
        Dictionary<MetricKey, BosunMetric> _rootNameAndTagsToMetric = new Dictionary<MetricKey, BosunMetric>(s_metricKeyComparer);

        readonly List<BosunMetric> _localMetrics = new List<BosunMetric>();
        readonly List<BosunMetric> _externalCounterMetrics = new List<BosunMetric>();

        readonly List<BosunMetric> _metricsNeedingPreSerialize = new List<BosunMetric>();
        // All of the names which have been claimed, including the metrics which may have multiple suffixes, mapped to their root metric name.
        // This is to prevent suffix collisions with other metrics.
        readonly Dictionary<string, string> _nameAndSuffixToRootName = new Dictionary<string, string>();

        readonly string _accessToken;
        readonly Func<string> _getAccessToken;

        readonly object _flushingLock = new object();
        readonly Timer _flushTimer;
        readonly Timer _reportingTimer;
        readonly Timer _metaDataTimer;

        readonly PayloadQueue _localMetricsQueue;
        readonly PayloadQueue _externalCounterQueue;

        static readonly AsyncCallback s_asyncNoopCallback = AsyncNoopCallback;

        internal Dictionary<Type, List<BosunTag>> TagsByTypeCache = new Dictionary<Type, List<BosunTag>>();

        /// <summary>
        /// If provided, all metric names will be prefixed with this value. This gives you the ability to keyspace your application. For example, you might
        /// want to use something like "app1.".
        /// </summary>
        public string MetricsNamePrefix { get; }
        /// <summary>
        /// The url of the Bosun API. No path is required. If this is null, metrics will be discarded instead of sent to Bosun.
        /// </summary>
        public Uri BosunUrl { get; set; }
        /// <summary>
        /// If the url for the Bosun API can change, provide a function which will be called before each API request. This takes precedence over the BosunUrl
        /// option. If this function returns null, the request will not be made, and the batch of metrics which would have been sent will be discarded.
        /// </summary>
        public Func<Uri> GetBosunUrl { get; set; }
        /// <summary>
        /// If true, BosunReporter will generate an exception every time posting to the Bosun API fails with a server error (response code 5xx).
        /// </summary>
        public bool ThrowOnPostFail { get; set; }
        /// <summary>
        /// If true, BosunReporter will generate an exception when the metric queue is full. This would most commonly be caused by an extended outage of the
        /// Bosun API. It is an indication that data is likely being lost.
        /// </summary>
        public bool ThrowOnQueueFull { get; set; }
        /// <summary>
        /// The length of time between metric reports (snapshots).
        /// </summary>
        public TimeSpan ReportingInterval { get; }
        /// <summary>
        /// Enables sending metrics to the /api/count route on OpenTSDB relays which support external counters. External counters don't reset when applications
        /// reload, and are intended for low-volume metrics. For high-volume metrics, use normal counters.
        /// </summary>
        public bool EnableExternalCounters { get; set; }
        /// <summary>
        /// Allows you to specify a function which takes a property name and returns a tag name. This may be useful if you want to convert PropertyName to
        /// property_name or similar transformations. This function does not apply to any tag names which are set manually via the BosunTag attribute.
        /// </summary>
        public Func<string, string> PropertyToTagName { get; }
        /// <summary>
        /// Allows you to specify a function which takes a tag name and value, and returns a possibly altered value. This could be used as a global sanitizer
        /// or normalizer. It is applied to all tag values, including default tags. If the return value is not a valid OpenTSDB tag, an exception will be
        /// thrown. Null values are possible for the tagValue argument, so be sure to handle nulls appropriately.
        /// </summary>
        public TagValueConverterDelegate TagValueConverter { get; }
        /// <summary>
        /// A list of tag names/values which will be automatically inculuded on every metric. The IgnoreDefaultTags attribute can be used on classes inheriting
        /// from BosunMetric to exclude default tags. If an inherited class has a conflicting BosunTag field, it will override the default tag value. Default
        /// tags will generally not be included in metadata.
        /// </summary>
        public ReadOnlyDictionary<string, string> DefaultTags { get; private set; }

        /// <summary>
        /// True if <see cref="Shutdown"/> has been called on this collector.
        /// </summary>
        public bool ShutdownCalled { get; private set; }

        // insights
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

        /// <summary>
        /// Information about the last time that metrics were serialized (in preparation for posting to the Bosun API).
        /// </summary>
        public AfterSerializationInfo LastSerializationInfo { get; private set; }

        /// <summary>
        /// Exceptions which occur on a background thread within BosunReporter will be passed to this delegate.
        /// </summary>
        public Action<Exception> ExceptionHandler { get; }

        /// <summary>
        /// The number of payloads which can be queued for sending to Bosun. If the queue is full, additional payloads will be dropped. External counters have
        /// their own dedicated queue of the same size. So, in theory, there could be up to (MaxPendingPayloads * 2) waiting to send.
        /// </summary>
        public int MaxPendingPayloads
        {
            get { return _localMetricsQueue.MaxPendingPayloads; }
            set
            {
                if (value < 1)
                    throw new Exception("Cannot set MaxPendingPayloads to less than 1.");

                _localMetricsQueue.MaxPendingPayloads = value;
                _externalCounterQueue.MaxPendingPayloads = value;
            }
        }

        /// <summary>
        /// The maximum size of a single payload to Bosun. It's best practice to set this to a size which can fit inside a single TCP packet. HTTP Headers
        /// are not included in this size, so it's best to pick a value a bit smaller than the size of your TCP packets. However, this property cannot be set
        /// to a size less than 1000.
        /// </summary>
        public int MaxPayloadSize
        {
            get { return _localMetricsQueue.PayloadSize; }
            set
            {
                if (value < 1000)
                    throw new Exception("Cannot set a MaxPayloadSize less than 1000 bytes.");

                _localMetricsQueue.PayloadSize = value;
                _externalCounterQueue.PayloadSize = value;
            }
        }

        /// <summary>
        /// The number of payloads currently queued for sending to Bosun. This includes external counter payloads.
        /// </summary>
        public int PendingPayloadCount => _localMetricsQueue.PendingPayloadsCount + _externalCounterQueue.PendingPayloadsCount;

        /// <summary>
        /// An event called immediately before metrics are serialized. If you need to take a pre-serialization action on an individual metric, you should
        /// consider overriding <see cref="BosunMetric.PreSerialize"/> instead, which is called in parallel for all metrics. This event occurs before
        /// PreSerialize is called.
        /// </summary>
        public event Action BeforeSerialization;
        /// <summary>
        /// An event called immediately after metrics are serialized. It includes an argument with post-serialization information.
        /// </summary>
        public event Action<AfterSerializationInfo> AfterSerialization;
        /// <summary>
        /// An event called immediately after metrics are posted to the Bosun API. It includes an argument with information about the POST.
        /// </summary>
        public event Action<AfterPostInfo> AfterPost;

        /// <summary>
        /// Enumerable of all metrics managed by this collector.
        /// </summary>
        public IEnumerable<BosunMetric> Metrics => _rootNameAndTagsToMetric.Values.AsEnumerable();

        /// <summary>
        /// Instantiates a new collector (the primary class of BosunReporter). You should typically only instantiate one collector for the lifetime of your
        /// application. It will manage the serialization of metrics and sending data to Bosun.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="exceptionHandler"></param>
        public MetricsCollector(BosunOptions options, Action<Exception> exceptionHandler)
        {
            ExceptionHandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));

            if (options.ReportingInterval < TimeSpan.FromSeconds(1))
                throw new InvalidOperationException("options.ReportingInterval cannot be less than one second");

            if (options.MetadataReportingDelay < TimeSpan.Zero)
                throw new InvalidOperationException("options.MetadataReportingDelay cannot be less than TimeSpan.Zero");

            if (options.MetadataReportingInterval < TimeSpan.Zero)
                throw new InvalidOperationException("options.MetadataReportingInterval cannot be less than TimeSpan.Zero");

            _localMetricsQueue = new PayloadQueue(QueueType.Local);
            _externalCounterQueue = new PayloadQueue(QueueType.ExternalCounters);

            _localMetricsQueue.PayloadDropped += OnPayloadDropped;
            _externalCounterQueue.PayloadDropped += OnPayloadDropped;

            // these two setters actually update the queues themselves
            MaxPayloadSize = options.MaxPayloadSize;
            MaxPendingPayloads = options.MaxPendingPayloads;

            MetricsNamePrefix = options.MetricsNamePrefix ?? "";
            if (MetricsNamePrefix != "" && !BosunValidation.IsValidMetricName(MetricsNamePrefix))
                throw new Exception("\"" + MetricsNamePrefix + "\" is not a valid metric name prefix.");

            GetBosunUrl = options.GetBosunUrl;
            BosunUrl = GetBosunUrl == null ? options.BosunUrl : GetBosunUrl();

            _accessToken = options.AccessToken;
            _getAccessToken = options.GetAccessToken;

            ThrowOnPostFail = options.ThrowOnPostFail;
            ThrowOnQueueFull = options.ThrowOnQueueFull;
            ReportingInterval = options.ReportingInterval;
            EnableExternalCounters = options.EnableExternalCounters;
            PropertyToTagName = options.PropertyToTagName;
            TagValueConverter = options.TagValueConverter;
            DefaultTags = ValidateDefaultTags(options.DefaultTags);

            // start continuous queue-flushing
            _flushTimer = new Timer(Flush, true, 1000, 1000);

            // start reporting timer
            _reportingTimer = new Timer(Snapshot, true, ReportingInterval, ReportingInterval);

            // metadata timer
            if (options.MetadataReportingInterval > TimeSpan.Zero)
                _metaDataTimer = new Timer(PostMetadataFromTimer, true, options.MetadataReportingDelay, options.MetadataReportingInterval);
        }

        /// <summary>
        /// Attempts to get basic information about a metric, by name. The global prefix <see cref="MetricsNamePrefix"/> is prepended to the name before
        /// attempting to retrieve the info. Returns false if no metric by that name exists.
        /// </summary>
        public bool TryGetMetricInfo(string name, out Type type, out string unit)
        {
            return TryGetMetricWithoutPrefixInfo(MetricsNamePrefix + name, out type, out unit);
        }

        /// <summary>
        /// Attempts to get basic information about a metric, by name. The global prefix <see cref="MetricsNamePrefix"/> is NOT applied to the name. Return
        /// false if no metric by that name exists.
        /// </summary>
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

        ReadOnlyDictionary<string, string> ValidateDefaultTags(Dictionary<string, string> tags)
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

        /// <summary>
        /// Binds a given metric name to a specific data model. Metrics with this name will only be allowed to use the type <paramref name="type"/>. Calling
        /// this method is usually not necessary. A metric will be bound to the type that it is first instantiated with.
        /// 
        /// The global prefix <see cref="MetricsNamePrefix"/> is prepended to the name before attempting to bind the metric.
        /// </summary>
        public void BindMetric(string name, string unit, Type type)
        {
            BindMetricWithoutPrefix(MetricsNamePrefix + name, unit, type);
        }

        /// <summary>
        /// Binds a given metric name to a specific data model. Metrics with this name will only be allowed to use the type <paramref name="type"/>. Calling
        /// this method is usually not necessary. A metric will be bound to the type that it is first instantiated with.
        /// </summary>
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
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The <see cref="MetricsNamePrefix"/> will be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        public T CreateMetric<T>(string name, string unit, string description, Func<T> metricFactory) where T : BosunMetric
        {
            return GetMetricInternal(name, true, unit, description, metricFactory(), true);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The <see cref="MetricsNamePrefix"/> will be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        public T CreateMetric<T>(string name, string unit, string description, T metric = null) where T : BosunMetric
        {
            return GetMetricInternal(name, true, unit, description, metric, true);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The <see cref="MetricsNamePrefix"/> will NOT be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        public T CreateMetricWithoutPrefix<T>(string name, string unit, string description, Func<T> metricFactory) where T : BosunMetric
        {
            return GetMetricInternal(name, false, unit, description, metricFactory(), true);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The <see cref="MetricsNamePrefix"/> will NOT be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        public T CreateMetricWithoutPrefix<T>(string name, string unit, string description, T metric = null) where T : BosunMetric
        {
            return GetMetricInternal(name, false, unit, description, metric, true);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned. The <see cref="MetricsNamePrefix"/> will be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        public T GetMetric<T>(string name, string unit, string description, Func<T> metricFactory) where T : BosunMetric
        {
            return GetMetricInternal(name, true, unit, description, metricFactory(), false);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned. The <see cref="MetricsNamePrefix"/> will be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        public T GetMetric<T>(string name, string unit, string description, T metric = null) where T : BosunMetric
        {
            return GetMetricInternal(name, true, unit, description, metric, false);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned. The <see cref="MetricsNamePrefix"/> will NOT be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        public T GetMetricWithoutPrefix<T>(string name, string unit, string description, Func<T> metricFactory) where T : BosunMetric
        {
            return GetMetricInternal(name, false, unit, description, metricFactory(), false);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned. The <see cref="MetricsNamePrefix"/> will NOT be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        public T GetMetricWithoutPrefix<T>(string name, string unit, string description, T metric = null) where T : BosunMetric
        {
            return GetMetricInternal(name, false, unit, description, metric, false);
        }

        T GetMetricInternal<T>(string name, bool addPrefix, string unit, string description, T metric, bool mustBeNew) where T : BosunMetric
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

            metric.LoadSuffixes();

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
                foreach (var s in metric.SuffixesArray)
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

                foreach (var s in metric.SuffixesArray)
                {
                    _nameAndSuffixToRootName[name + s] = name;
                }

                // claim the root type
                _rootNameToInfo[name] = new RootMetricInfo { Type = metricType, Unit = unit };

                // see if this metric name and tag combination already exists
                var key = metric.GetMetricKey();
                if (_rootNameAndTagsToMetric.ContainsKey(key))
                {
                    if (mustBeNew)
                        throw new Exception($"Attempted to create duplicate metric with name \"{name}\" and tags {metric.TagsJson}.");

                    return (T) _rootNameAndTagsToMetric[key];
                }

                // metric doesn't exist yet.
                _rootNameAndTagsToMetric[key] = metric;
                metric.IsAttached = true;

                var needsPreSerialize = metric.NeedsPreSerializeCalled();
                if (needsPreSerialize)
                    _metricsNeedingPreSerialize.Add(metric);

                var isExternal = metric.IsExternalCounter();
                if (isExternal)
                    _externalCounterMetrics.Add(metric);
                else
                    _localMetrics.Add(metric);

                if (metric.SerializeInitialValue)
                {
                    MetricWriter writer = null;
                    try
                    {
                        var queue = isExternal ? _externalCounterQueue : _localMetricsQueue;
                        writer = queue.GetWriter();

                        if (needsPreSerialize)
                            metric.PreSerializeInternal();

                        metric.SerializeInternal(writer, DateTime.UtcNow);
                    }
                    finally
                    {
                        writer?.EndBatch();
                    }
                }

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

        /// <summary>
        /// Updates the tag name/values which are applied to all metrics by default. This update must not cause any uniqueness violations, otherwise an
        /// exception will be thrown.
        /// </summary>
        /// <param name="defaultTags"></param>
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
                var rootNameAndTagsToMetric = new Dictionary<MetricKey, BosunMetric>(s_metricKeyComparer);
                var tagsByTypeCache = new Dictionary<Type, List<BosunTag>>();
                var tagsJsonByKey = new Dictionary<MetricKey, string>(s_metricKeyComparer);
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

        static bool AreIdenticalTags(ReadOnlyDictionary<string, string> a, ReadOnlyDictionary<string, string> b)
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

        void Snapshot(object isCalledFromTimer)
        {
            if ((bool)isCalledFromTimer && ShutdownCalled) // don't perform timer actions if we're shutting down
                return;

            if (GetBosunUrl != null)
                BosunUrl = GetBosunUrl();

            try
            {
                if (BeforeSerialization != null && BeforeSerialization.GetInvocationList().Length != 0)
                    BeforeSerialization();

                var sw = new StopwatchStruct();
                sw.Start();
                int metricsCount, bytesWritten;
                SerializeMetrics(out metricsCount, out bytesWritten);
                sw.Stop();

                var info = new AfterSerializationInfo
                {
                    Count = metricsCount,
                    BytesWritten = bytesWritten,
                    MillisecondsDuration = sw.GetElapsedMilliseconds(),
                };

                LastSerializationInfo = info;
                AfterSerialization?.Invoke(info);
            }
            catch (Exception ex)
            {
                PossiblyLogException(ex);
            }
        }

        void Flush(object isCalledFromTimer)
        {
            if ((bool)isCalledFromTimer && ShutdownCalled) // don't perform timer actions if we're shutting down
                return;

            var lockTaken = false;
            try
            {
                lockTaken = Monitor.TryEnter(_flushingLock);

                // the lock prevents calls to Flush from stacking up, but we skip this check if we're in draining mode
                if (!lockTaken && !ShutdownCalled)
                {
                    Debug.WriteLine("BosunReporter: Flush already in progress (skipping).");
                    return;
                }

                FlushPayloadQueue(_localMetricsQueue);

                if (EnableExternalCounters)
                    FlushPayloadQueue(_externalCounterQueue);
                else
                    _externalCounterQueue.Clear();

                // todo: post metadata
            }
            catch (Exception ex)
            {
                // this should never actually hit, but it's a nice safeguard since an uncaught exception on a background thread will crash the process.
                PossiblyLogException(ex);
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(_flushingLock);
            }
        }

        void FlushPayloadQueue(PayloadQueue queue)
        {
            if (queue.PendingPayloadsCount == 0)
                return;

            if (!ShutdownCalled && queue.SuspendFlushingUntil > DateTime.UtcNow)
                return;

            var url = GetBosunUrl != null ? GetBosunUrl() : BosunUrl;
            if (url == null)
            {
                Debug.WriteLine("BosunReporter: BosunUrl is null. Dropping data.");
                return;
            }

            while (queue.PendingPayloadsCount > 0)
            {
                if (!FlushPayload(url, queue))
                    break;
            }
        }

        bool FlushPayload(Uri url, PayloadQueue queue)
        {
            var payload = queue.DequeuePendingPayload();
            if (payload == null)
                return false;

            var metricsCount = payload.MetricsCount;
            var bytes = payload.Used;

            Debug.WriteLine($"BosunReporter: Flushing metrics batch. {metricsCount} metrics. {bytes} bytes.");

            var info = new AfterPostInfo();
            var timer = new StopwatchStruct();
            try
            {
                timer.Start();
                PostToBosun(url, queue.UrlPath, true, sw => sw.Write(payload.Data, 0, payload.Used));
                timer.Stop();

                PostSuccessCount++;
                TotalMetricsPosted += payload.MetricsCount;
                queue.ReleasePayload(payload);
                return true;
            }
            catch (Exception ex)
            {
                timer.Stop();
                // posting to Bosun failed, so put the batch back in the queue to try again later
                Debug.WriteLine("BosunReporter: Posting to the Bosun API failed. Pushing metrics back onto the queue.");
                PostFailCount++;
                info.Exception = ex;
                queue.AddPendingPayload(payload);
                queue.SuspendFlushingUntil = DateTime.UtcNow.AddSeconds(10); // back off for 10 seconds after a failed request
                PossiblyLogException(ex);
                return false;
            }
            finally
            {
                // don't use the payload variable in this block - it may have been released back to the pool by now
                info.Count = metricsCount;
                info.BytesWritten = bytes;
                info.MillisecondsDuration = timer.GetElapsedMilliseconds();

                // Use BeginInvoke here to invoke the event listeners asynchronously.
                // We're inside a lock, so calling the listeners synchronously would put us at risk of a deadlock.
                AfterPost?.BeginInvoke(info, s_asyncNoopCallback, null);
            }
        }

        static void AsyncNoopCallback(IAsyncResult result) { }

        delegate void ApiPostWriter(Stream sw);

        void PostToBosun(Uri bosunUrl, string path, bool gzip, ApiPostWriter postWriter)
        {
            var url = new Uri(bosunUrl, path);

            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            if (gzip)
                request.Headers["Content-Encoding"] = "gzip";

            // support for http://username:password@domain.com, by default this does not work
            var userInfo = url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
            if (!string.IsNullOrEmpty(userInfo))
            {
                var auth = "Basic " + Convert.ToBase64String(Encoding.Default.GetBytes(userInfo));
                request.Headers["Authorization"] = auth;
            }

            var accessToken = _getAccessToken != null ? _getAccessToken() : _accessToken;
            if (!string.IsNullOrEmpty(accessToken))
                request.Headers["X-Access-Token"] = accessToken;

            try
            {
                using (var stream = request.GetRequestStream())
                {
                    if (gzip)
                    {
                        using (var gzipStream = new GZipStream(stream, CompressionMode.Compress))
                        {
                            postWriter(gzipStream);
                        }
                    }
                    else
                    {
                        postWriter(stream);
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

        void SerializeMetrics(out int metricsCount, out int bytesWritten)
        {
            lock (_metricsLock)
            {
                var timestamp = DateTime.UtcNow;
                if (_metricsNeedingPreSerialize.Count > 0)
                {
                    Parallel.ForEach(_metricsNeedingPreSerialize, m => m.PreSerializeInternal());
                }

                metricsCount = 0;
                bytesWritten = 0;
                SerializeMetricListToWriter(_localMetricsQueue, _localMetrics, timestamp, ref metricsCount, ref bytesWritten);
                SerializeMetricListToWriter(_externalCounterQueue, _externalCounterMetrics, timestamp, ref metricsCount, ref bytesWritten);
            }
        }

        static void SerializeMetricListToWriter(PayloadQueue queue, List<BosunMetric> metrics, DateTime timestamp, ref int metricsCount, ref int bytesWritten)
        {
            if (metrics.Count == 0)
                return;

            var writer = queue.GetWriter();

            try
            {
                foreach (var m in metrics)
                {
                    m.SerializeInternal(writer, timestamp);

                    if (queue.IsFull)
                        break;
                }

                metricsCount += writer.MetricsCount;
                bytesWritten += writer.TotalBytesWritten;
            }
            finally
            {
                writer.EndBatch();
            }
        }

        void PostMetadataFromTimer(object _)
        {
            if (ShutdownCalled) // don't report any more meta data if we're shutting down
                return;

            var url = BosunUrl;
            if (url == null)
            {
                Debug.WriteLine("BosunReporter: BosunUrl is null. Not sending metadata.");
                return;
            }

            try
            {
                PostMetadataInternal(url);
            }
            catch (Exception ex)
            {
                PossiblyLogException(ex);
            }
        }

        /// <summary>
        /// Posts metadata to the Bosun relay endpoint. Returns the JSON which was sent to Bosun. This method typically doesn't need to be called directly.
        /// Metadata is regularly posted on a schedule determined by <see cref="BosunOptions"/> when initializing the <see cref="MetricsCollector"/>.
        /// </summary>
        public string PostMetadata()
        {
            var url = BosunUrl;
            if (url == null)
                throw new Exception("Cannot send metadata. BosunUrl is null");

            return PostMetadataInternal(url);
        }

        string PostMetadataInternal(Uri bosunUrl)
        {
            Debug.WriteLine("BosunReporter: Gathering metadata.");
            var metaJson = GatherMetaData();
            Debug.WriteLine("BosunReporter: Sending metadata.");
            PostToBosun(bosunUrl, "/api/metadata/put", false, stream =>
            {
                using (var sw = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    sw.Write(metaJson);
                }
            });

            return metaJson;
        }

        string GatherMetaData()
        {
            var json = new StringBuilder();

            lock (_metricsLock)
            {
                foreach (var metric in Metrics)
                {
                    if (metric == null)
                        continue;

                    foreach (var meta in metric.GetMetaData())
                    {
                        json.Append(",{\"Metric\":\"");
                        json.Append(meta.Metric);
                        json.Append("\",\"Name\":\"");
                        json.Append(meta.Name);
                        json.Append("\",\"Value\":");
                        JsonHelper.WriteString(json, meta.Value);

                        if (meta.Tags != null)
                        {
                            json.Append(",\"Tags\":");
                            json.Append(meta.Tags);
                        }

                        json.Append("}\n");
                    }
                }
            }

            if (json.Length == 0)
                return "[]";

            json[0] = '['; // replace the first comma with an open bracket
            json.Append(']');

            return json.ToString();
        }

        void OnPayloadDropped(BosunQueueFullException ex)
        {
            PossiblyLogException(ex);
        }

        void PossiblyLogException(Exception ex)
        {
            if (!ShouldThrowException(ex))
                return;

            try
            {
                ExceptionHandler(ex);
            }
            catch (Exception) { } // there's nothing else we can do if the user-supplied exception handler itself throws an exception
        }

        bool ShouldThrowException(Exception ex)
        {
            var post = ex as BosunPostException;
            if (post != null)
            {
                if (ThrowOnPostFail)
                    return true;

                if (post.StatusCode.HasValue)
                {
                    var status = (int)post.StatusCode;
                    return status < 500 || status >= 600; // always want to send the exception when it's a non-500
                }

                return false;
            }

            if (ex is BosunQueueFullException)
                return ThrowOnQueueFull;

            return true;
        }
    }

    /// <summary>
    /// Information about a metrics serialization pass.
    /// </summary>
    public class AfterSerializationInfo
    {
        /// <summary>
        /// The number of data points serialized. The could be less than or greater than the number of metrics managed by the collector.
        /// </summary>
        public int Count { get; internal set; }
        /// <summary>
        /// The number of bytes written to payload(s).
        /// </summary>
        public int BytesWritten { get; internal set; }
        /// <summary>
        /// The duration of the serialization pass, in milliseconds.
        /// </summary>
        public double MillisecondsDuration { get; internal set; }
        /// <summary>
        /// The time serialization started.
        /// </summary>
        public DateTime StartTime { get; }

        internal AfterSerializationInfo()
        {
            StartTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Information about a POST to the Bosun API.
    /// </summary>
    public class AfterPostInfo
    {
        /// <summary>
        /// The number of data points sent.
        /// </summary>
        public int Count { get; internal set; }
        /// <summary>
        /// The number of bytes in the payload. This does not include HTTP header bytes.
        /// </summary>
        public int BytesWritten { get; internal set; }
        /// <summary>
        /// The duration of the POST, in milliseconds.
        /// </summary>
        public double MillisecondsDuration { get; internal set; }
        /// <summary>
        /// True if the POST was successful. If false, <see cref="Exception"/> will be non-null.
        /// </summary>
        public bool Successful => Exception == null;
        /// <summary>
        /// Information about a POST failure, if applicable. Otherwise, null.
        /// </summary>
        public Exception Exception { get; internal set; }
        /// <summary>
        /// The time the POST was initiated.
        /// </summary>
        public DateTime StartTime { get; }

        internal AfterPostInfo()
        {
            StartTime = DateTime.UtcNow;
        }
    }
}
