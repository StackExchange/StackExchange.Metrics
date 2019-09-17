using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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

        readonly object _metricsLock = new object();
        // all of the first-class names which have been claimed (excluding suffixes in aggregate gauges)
        readonly Dictionary<string, RootMetricInfo> _rootNameToInfo = new Dictionary<string, RootMetricInfo>();
        readonly MetricEndpoint[] _endpoints;
        // this dictionary is to avoid duplicate metrics
        Dictionary<MetricKey, BosunMetric> _rootNameAndTagsToMetric = new Dictionary<MetricKey, BosunMetric>(MetricKeyComparer.Default);

        readonly List<BosunMetric> _metrics = new List<BosunMetric>();

        bool _hasNewMetadata = false;
        DateTime _lastMetadataFlushTime = DateTime.MinValue;
        CancellationTokenSource _shutdownTokenSource;

        readonly List<BosunMetric> _metricsNeedingPreSerialize = new List<BosunMetric>();
        // All of the names which have been claimed, including the metrics which may have multiple suffixes, mapped to their root metric name.
        // This is to prevent suffix collisions with other metrics.
        readonly Dictionary<string, string> _nameAndSuffixToRootName = new Dictionary<string, string>();

        readonly Task _flushTask;
        readonly Task _reportingTask;
        readonly TimeSpan _delayBetweenRetries;
        readonly int _maxRetries;

        internal Dictionary<Type, List<BosunTag>> TagsByTypeCache = new Dictionary<Type, List<BosunTag>>();

        /// <summary>
        /// If provided, all metric names will be prefixed with this value. This gives you the ability to keyspace your application. For example, you might
        /// want to use something like "app1.".
        /// </summary>
        public string MetricsNamePrefix { get; }
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
        /// The length of time between flush operations to an endpoint.
        /// </summary>
        public TimeSpan FlushInterval { get; }
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
        public bool ShutdownCalled => _shutdownTokenSource.IsCancellationRequested;

        /// <summary>
        /// Total number of data points successfully sent fo Bosun. This includes external counter data points.
        /// </summary>
        public long TotalMetricsPosted { get; private set; }
        /// <summary>
        /// The number of times an HTTP POST to one of Bosun's metrics endpoints succeeded. This includes external counter POSTs.
        /// </summary>
        public int PostSuccessCount { get; private set; }
        /// <summary>
        /// The number of times an HTTP POST to Bosun's metrics endpoints failed. This includes external counter POSTs.
        /// </summary>
        public int PostFailCount { get; private set; }

        /// <summary>
        /// The last time metadata was sent to Bosun, or null if metadata has not been sent yet.
        /// </summary>
        public DateTime? LastMetadataFlushTime => _lastMetadataFlushTime == DateTime.MinValue ? (DateTime?)null : _lastMetadataFlushTime;

        /// <summary>
        /// Exceptions which occur on a background thread within BosunReporter will be passed to this delegate.
        /// </summary>
        public Action<Exception> ExceptionHandler { get; }

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
        public event Action<AfterSendInfo> AfterSend;

        /// <summary>
        /// Enumerable of all metrics managed by this collector.
        /// </summary>
        public IEnumerable<BosunMetric> Metrics => _rootNameAndTagsToMetric.Values.AsEnumerable();

        /// <summary>
        /// Enumerable of all endpoints managed by this collector.
        /// </summary>
        public IEnumerable<MetricEndpoint> Endpoints => _endpoints.AsEnumerable();

        /// <summary>
        /// Instantiates a new collector (the primary class of BosunReporter). You should typically only instantiate one collector for the lifetime of your
        /// application. It will manage the serialization of metrics and sending data to Bosun.
        /// </summary>
        /// <param name="options">
        /// <see cref="BosunOptions" /> representing the options to use for this collector.
        /// </param>
        public MetricsCollector(BosunOptions options)
        {
            ExceptionHandler = options.ExceptionHandler;

            if (options.SnapshotInterval < TimeSpan.FromSeconds(1))
                throw new InvalidOperationException("options.SnapshotInterval cannot be less than one second");

            MetricsNamePrefix = options.MetricsNamePrefix ?? "";
            if (MetricsNamePrefix != "" && !BosunValidation.IsValidMetricName(MetricsNamePrefix))
                throw new Exception("\"" + MetricsNamePrefix + "\" is not a valid metric name prefix.");

            _endpoints = options.Endpoints?.ToArray() ?? Array.Empty<MetricEndpoint>();

            ThrowOnPostFail = options.ThrowOnPostFail;
            ThrowOnQueueFull = options.ThrowOnQueueFull;
            ReportingInterval = options.SnapshotInterval;
            FlushInterval = options.FlushInterval;
            PropertyToTagName = options.PropertyToTagName;
            TagValueConverter = options.TagValueConverter;
            DefaultTags = ValidateDefaultTags(options.DefaultTags);

            _delayBetweenRetries = TimeSpan.FromSeconds(10);
            _maxRetries = 3;
            _shutdownTokenSource = new CancellationTokenSource();

            // start continuous queue-flushing
            _flushTask = Task.Run(
                async () =>
                {
                    while (!_shutdownTokenSource.IsCancellationRequested)
                    {
                        await Task.Delay(FlushInterval);

                        try
                        {
                            await FlushAsync(true);
                        }
                        catch (Exception ex)
                        {
                            SendExceptionToHandler(ex);
                        }
                    }
                });

            // start reporting timer
            _reportingTask = Task.Run(
                async () =>
                {
                    while (!_shutdownTokenSource.IsCancellationRequested)
                    {
                        await Task.Delay(ReportingInterval);

                        try
                        {
                            await SnapshotAsync(true);
                        }
                        catch (Exception ex)
                        {
                            SendExceptionToHandler(ex);
                        }
                    }
                });
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
            if (_rootNameToInfo.TryGetValue(name, out var rmi))
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
                if (_rootNameToInfo.TryGetValue(name, out var rmi))
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

                if (!type.IsSubclassOf(typeof(BosunMetric)))
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

            var metricType = typeof(T);
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
                if (_rootNameToInfo.TryGetValue(name, out var rmi))
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
                        throw new Exception($"Attempted to create duplicate metric with name \"{name}\" and tags {string.Join(", ", metric.Tags.Keys)}.");

                    return (T)_rootNameAndTagsToMetric[key];
                }

                // metric doesn't exist yet.
                _rootNameAndTagsToMetric[key] = metric;
                metric.IsAttached = true;
                _hasNewMetadata = true;

                var needsPreSerialize = metric.NeedsPreSerializeCalled();
                if (needsPreSerialize)
                    _metricsNeedingPreSerialize.Add(metric);

                _metrics.Add(metric);

                if (metric.SerializeInitialValue)
                {
                    if (needsPreSerialize)
                        metric.PreSerializeInternal();

                    foreach (var endpoint in _endpoints)
                    {
                        using (var batch = endpoint.Handler.BeginBatch())
                        {
                            try
                            {
                                metric.SerializeInternal(batch, DateTime.UtcNow);
                            }
                            catch (Exception ex)
                            {
                                SendExceptionToHandler(ex);
                            }
                        }
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
            _shutdownTokenSource.Cancel();
            _shutdownTokenSource = null;

            foreach (var endpoint in _endpoints)
            {
                endpoint.Handler.Dispose();
            }
        }

        Task SnapshotAsync(bool isCalledFromTimer)
        {
            if (isCalledFromTimer && ShutdownCalled) // don't perform timer actions if we're shutting down
                return Task.CompletedTask;

            try
            {
                if (BeforeSerialization != null && BeforeSerialization.GetInvocationList().Length != 0)
                    BeforeSerialization();

                IReadOnlyList<MetaData> metadata = Array.Empty<MetaData>();
                if (_hasNewMetadata || DateTime.UtcNow - _lastMetadataFlushTime >= TimeSpan.FromDays(1))
                {
                    metadata = GatherMetaData();
                }

                // prep all metrics for serialization
                var timestamp = DateTime.UtcNow;
                if (_metricsNeedingPreSerialize.Count > 0)
                {
                    Parallel.ForEach(_metricsNeedingPreSerialize, m => m.PreSerializeInternal());
                }

                var sw = new Stopwatch();
                foreach (var endpoint in _endpoints)
                {
                    sw.Restart();
                    SerializeMetrics(endpoint, timestamp, out var metricsCount, out var bytesWritten);
                    // We don't want to send metadata more frequently than the snapshot interval, so serialize it out if we need to
                    if (metadata.Count > 0)
                    {
                        SerializeMetadata(endpoint, metadata);
                    }
                    sw.Stop();

                    AfterSerialization?.Invoke(
                        new AfterSerializationInfo
                        {
                            Endpoint = endpoint.Name,
                            Count = metricsCount,
                            BytesWritten = bytesWritten,
                            Duration = sw.Elapsed,
                        });
                }
            }
            catch (Exception ex)
            {
                SendExceptionToHandler(ex);
            }

            return Task.CompletedTask;
        }

        async Task FlushAsync(bool isCalledFromTimer)
        {
            if (isCalledFromTimer && ShutdownCalled) // don't perform timer actions if we're shutting down
                return;

            try
            {
                if (_endpoints.Length == 0)
                {
                    Debug.WriteLine("BosunReporter: BosunUrl is null. Dropping data.");
                    return;
                }

                foreach (var endpoint in _endpoints)
                {
                    Debug.WriteLine($"BosunReporter: Flushing metrics for {endpoint.Name}");

                    await endpoint.Handler.FlushAsync(
                        _delayBetweenRetries,
                        _maxRetries,
                        // Use Task.Run here to invoke the event listeners asynchronously.
                        // We're inside a lock, so calling the listeners synchronously would put us at risk of a deadlock.
                        info => Task.Run(
                            () =>
                            {
                                info.Endpoint = endpoint.Name;
                                try
                                {
                                    AfterSend?.Invoke(info);
                                }
                                catch (Exception ex)
                                {
                                    SendExceptionToHandler(ex);
                                }
                            }
                        ),
                        ex => SendExceptionToHandler(ex)
                    );
                }
            }
            catch (Exception ex)
            {
                // this should never actually hit, but it's a nice safeguard since an uncaught exception on a background thread will crash the process.
                SendExceptionToHandler(ex);
            }
        }

        void SerializeMetrics(MetricEndpoint endpoint, DateTime timestamp, out long metricsCount, out long bytesWritten)
        {
            lock (_metricsLock)
            {
                metricsCount = 0;
                bytesWritten = 0;
                if (_metrics.Count == 0)
                    return;

                using (var batch = endpoint.Handler.BeginBatch())
                {
                    foreach (var m in _metrics)
                    {
                        try
                        {
                            m.SerializeInternal(batch, timestamp);
                        }
                        catch (Exception ex)
                        {
                            ex.Data["Endpoint.Name"] = endpoint.Name;
                            ex.Data["Endpoint.Type"] = endpoint.Handler.GetType();

                            SendExceptionToHandler(ex);
                        }
                    }

                    metricsCount += batch.MetricsWritten;
                    bytesWritten += batch.BytesWritten;
                }
            }
        }

        void SerializeMetadata(MetricEndpoint endpoint, IEnumerable<MetaData> metadata)
        {
            Debug.WriteLine("BosunReporter: Serializing metadata.");
            endpoint.Handler.SerializeMetadata(metadata);
            _lastMetadataFlushTime = DateTime.UtcNow;
            Debug.WriteLine("BosunReporter: Serialized metadata.");
        }

        IReadOnlyList<MetaData> GatherMetaData()
        {
            lock (_metricsLock)
            {
                var allMetadata = new List<MetaData>();
                foreach (var metric in Metrics)
                {
                    if (metric == null)
                        continue;

                    allMetadata.AddRange(metric.GetMetaData());
                }

                _hasNewMetadata = false;
                return allMetadata;
            }
        }

        void SendExceptionToHandler(Exception ex)
        {
            if (!ShouldSendException(ex))
                return;

            try
            {
                ExceptionHandler(ex);
            }
            catch (Exception) { } // there's nothing else we can do if the user-supplied exception handler itself throws an exception
        }

        bool ShouldSendException(Exception ex)
        {
            if (ex is BosunPostException post)
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
        /// Endpoint that we wrote data to.
        /// </summary>
        public string Endpoint { get; internal set; }
        /// <summary>
        /// The number of data points serialized. The could be less than or greater than the number of metrics managed by the collector.
        /// </summary>
        public long Count { get; internal set; }
        /// <summary>
        /// The number of bytes written to payload(s).
        /// </summary>
        public long BytesWritten { get; internal set; }
        /// <summary>
        /// The duration of the serialization pass.
        /// </summary>
        public TimeSpan Duration { get; internal set; }
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
    /// Information about a send to a metrics endpoint.
    /// </summary>
    public class AfterSendInfo
    {
        /// <summary>
        /// Endpoint that we sent data to.
        /// </summary>
        public string Endpoint { get; internal set; }
        /// <summary>
        /// Gets a <see cref="PayloadType" /> indicating the type of payload that was flushed.
        /// </summary>
        public PayloadType PayloadType { get; internal set; }
        /// <summary>
        /// The number of bytes in the payload. This does not include HTTP header bytes.
        /// </summary>
        public long BytesWritten { get; internal set; }
        /// <summary>
        /// The duration of the POST.
        /// </summary>
        public TimeSpan Duration { get; internal set; }
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

        internal AfterSendInfo()
        {
            StartTime = DateTime.UtcNow;
        }
    }
}
