using StackExchange.Metrics.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Metrics
{
    /// <summary>
    /// The primary class for collecting metrics. Use this class to create metrics for reporting to various metric handlers.
    /// </summary>
    public partial class MetricsCollector : IMetricsCollector
    {
        private class RootMetricInfo
        {
            public Type Type { get; set; }
            public string Unit { get; set; }
        }

        private readonly object _metricsLock = new object();

        // all of the first-class names which have been claimed (excluding suffixes in aggregate gauges)
        private readonly Dictionary<string, RootMetricInfo> _rootNameToInfo = new Dictionary<string, RootMetricInfo>();
        private readonly ImmutableArray<MetricEndpoint> _endpoints;
        private readonly ImmutableArray<IMetricSet> _sets;

        // this dictionary is to avoid duplicate metrics
        private readonly Dictionary<MetricKey, MetricBase> _rootNameAndTagsToMetric = new Dictionary<MetricKey, MetricBase>(MetricKeyComparer.Default);
        private readonly List<MetricBase> _metrics = new List<MetricBase>();
        private bool _hasNewMetadata = false;
        private DateTime _lastMetadataFlushTime = DateTime.MinValue;
        private readonly CancellationTokenSource _shutdownTokenSource;
        private readonly List<MetricBase> _metricsNeedingPreSerialize = new List<MetricBase>();

        // All of the names which have been claimed, including the metrics which may have multiple suffixes, mapped to their root metric name.
        // This is to prevent suffix collisions with other metrics.
        private readonly Dictionary<string, string> _nameAndSuffixToRootName = new Dictionary<string, string>();
        private readonly Task _flushTask;
        private readonly Task _reportingTask;
        private readonly int _maxRetries;

        internal Dictionary<Type, List<MetricTag>> TagsByTypeCache = new Dictionary<Type, List<MetricTag>>();

        /// <summary>
        /// If provided, all metric names will be prefixed with this value. This gives you the ability to keyspace your application. For example, you might
        /// want to use something like "app1.".
        /// </summary>
        public string MetricsNamePrefix { get; }
        /// <summary>
        /// If true, we will generate an exception every time posting to the a metrics endpoint fails with a server error (response code 5xx).
        /// </summary>
        public bool ThrowOnPostFail { get; set; }
        /// <summary>
        /// If true, we will generate an exception when the metric queue is full. This would most commonly be caused by an extended outage of the
        /// a metric handler. It is an indication that data is likely being lost.
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
        /// The length of time to wait before retrying a failed flush operation to an endpoint.
        /// </summary>
        public TimeSpan RetryInterval { get; }
        /// <summary>
        /// Allows you to specify a function which takes a property name and returns a tag name. This may be useful if you want to convert PropertyName to
        /// property_name or similar transformations. This function does not apply to any tag names which are set manually via the MetricTag attribute.
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
        /// from MetricBase to exclude default tags. If an inherited class has a conflicting MetricTag field, it will override the default tag value. Default
        /// tags will generally not be included in metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> DefaultTags { get; private set; }

        /// <summary>
        /// True if <see cref="Shutdown"/> has been called on this collector.
        /// </summary>
        public bool ShutdownCalled => _shutdownTokenSource?.IsCancellationRequested ?? true;

        /// <summary>
        /// Exceptions which occur on a background thread within the collector will be passed to this delegate.
        /// </summary>
        public Action<Exception> ExceptionHandler { get; }

        /// <summary>
        /// An event called immediately before metrics are serialized. If you need to take a pre-serialization action on an individual metric, you should
        /// consider overriding <see cref="MetricBase.PreSerialize"/> instead, which is called in parallel for all metrics. This event occurs before
        /// PreSerialize is called.
        /// </summary>
        public event Action BeforeSerialization;
        /// <summary>
        /// An event called immediately after metrics are serialized. It includes an argument with post-serialization information.
        /// </summary>
        public event Action<AfterSerializationInfo> AfterSerialization;
        /// <summary>
        /// An event called immediately after metrics are posted to a metric handler. It includes an argument with information about the POST.
        /// </summary>
        public event Action<AfterSendInfo> AfterSend;

        /// <summary>
        /// Enumerable of all metrics managed by this collector.
        /// </summary>
        public IEnumerable<MetricBase> Metrics => _rootNameAndTagsToMetric.Values.AsEnumerable();

        /// <summary>
        /// Enumerable of all endpoints managed by this collector.
        /// </summary>
        public IEnumerable<MetricEndpoint> Endpoints => _endpoints.AsEnumerable();

        /// <summary>
        /// Enumerable of all sets managed by this collector.
        /// </summary>
        public IEnumerable<IMetricSet> Sets => _sets.AsEnumerable();

        /// <summary>
        /// Instantiates a new collector. You should typically only instantiate one collector for the lifetime of your
        /// application. It will manage the serialization of metrics and sending data to metric handlers.
        /// </summary>
        /// <param name="options">
        /// <see cref="MetricsCollectorOptions" /> representing the options to use for this collector.
        /// </param>
        public MetricsCollector(MetricsCollectorOptions options)
        {
            ExceptionHandler = options.ExceptionHandler ?? (_ => { });
            MetricsNamePrefix = options.MetricsNamePrefix ?? "";
            if (MetricsNamePrefix != "" && !MetricValidation.IsValidMetricName(MetricsNamePrefix))
                throw new Exception("\"" + MetricsNamePrefix + "\" is not a valid metric name prefix.");

            _endpoints = options.Endpoints?.ToImmutableArray() ?? ImmutableArray<MetricEndpoint>.Empty;
            _sets = options.Sets?.ToImmutableArray() ?? ImmutableArray<IMetricSet>.Empty;

            ThrowOnPostFail = options.ThrowOnPostFail;
            ThrowOnQueueFull = options.ThrowOnQueueFull;
            ReportingInterval = options.SnapshotInterval;
            FlushInterval = options.FlushInterval;
            RetryInterval = options.RetryInterval;
            PropertyToTagName = options.PropertyToTagName;
            TagValueConverter = options.TagValueConverter;
            DefaultTags = ValidateDefaultTags(options.DefaultTags);

            _maxRetries = 3;
            _shutdownTokenSource = new CancellationTokenSource();

            // initialize any metric sets
            foreach (var set in _sets)
            {
                set.Initialize(this);
            }

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
        /// Attempts to get basic information about a metric, by name. Returns false if no metric by that name exists.
        /// </summary>
        public bool TryGetMetricInfo(string name, out Type type, out string unit, bool includePrefix = true)
        {
            if (_rootNameToInfo.TryGetValue(includePrefix ? MetricsNamePrefix + name : name, out var rmi))
            {
                type = rmi.Type;
                unit = rmi.Unit;
                return true;
            }

            type = null;
            unit = null;
            return false;
        }

        private IReadOnlyDictionary<string, string> ValidateDefaultTags(IReadOnlyDictionary<string, string> tags)
        {
            var defaultTags = tags?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty;
            var defaultTagBuilder = defaultTags.ToBuilder();
            foreach (var key in defaultTags.Keys.ToArray())
            {
                if (!MetricValidation.IsValidTagName(key))
                    throw new Exception($"\"{key}\" is not a valid tag name.");

                if (TagValueConverter != null)
                    defaultTagBuilder[key] = TagValueConverter(key, defaultTags[key]);

                if (!MetricValidation.IsValidTagValue(defaultTags[key]))
                    throw new Exception($"\"{defaultTags[key]}\" is not a valid tag value.");
            }

            return defaultTagBuilder.ToImmutable();
        }

        /// <summary>
        /// Binds a given metric name to a specific data model. Metrics with this name will only be allowed to use the type <paramref name="type"/>. Calling
        /// this method is usually not necessary. A metric will be bound to the type that it is first instantiated with.
        /// </summary>
        public void BindMetric(string name, string unit, Type type, bool includePrefix = true)
        {
            if (includePrefix)
            {
                name = MetricsNamePrefix + name;
            }

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

                if (!type.IsSubclassOf(typeof(MetricBase)))
                {
                    throw new Exception($"Cannot bind metric \"{name}\" to Type {type.FullName}. It does not inherit from MetricBase.");
                }

                _rootNameToInfo[name] = new RootMetricInfo { Type = type, Unit = unit };
            }
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// </summary>
        /// <param name="name">The metric name. If <paramref name="includePrefix"/>, global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        /// <param name="includePrefix">Whether the <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.</param>
        public T CreateMetric<T>(string name, string unit, string description, Func<T> metricFactory, bool includePrefix = true) where T : MetricBase
        {
            return GetMetricInternal(name, true, unit, description, metricFactory(), true);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// </summary>
        /// <param name="name">The metric name. If <paramref name="includePrefix"/>, global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        /// <param name="includePrefix">Whether the <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.</param>
        public T CreateMetric<T>(string name, string unit, string description, T metric = null, bool includePrefix = true) where T : MetricBase
        {
            return GetMetricInternal(name, includePrefix, unit, description, metric, true);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned.
        /// </summary>
        /// <param name="name">The metric name. If <paramref name="includePrefix"/>, global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        /// <param name="includePrefix">Whether the <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.</param>
        public T GetMetric<T>(string name, string unit, string description, Func<T> metricFactory, bool includePrefix = true) where T : MetricBase
        {
            return GetMetricInternal(name, includePrefix, unit, description, metricFactory(), false);
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned.
        /// </summary>
        /// <param name="name">The metric name. If <paramref name="includePrefix"/>, global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        /// <param name="includePrefix">Whether the <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.</param>
        public T GetMetric<T>(string name, string unit, string description, T metric = null, bool includePrefix = true) where T : MetricBase
        {
            return GetMetricInternal(name, includePrefix, unit, description, metric, false);
        }

        private T GetMetricInternal<T>(string name, bool addPrefix, string unit, string description, T metric, bool mustBeNew) where T : MetricBase
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
                    if (!MetricValidation.IsValidMetricName(ns))
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
            Debug.WriteLine("StackExchange.Metrics: Shutting down MetricsCollector.");
            _shutdownTokenSource.Cancel();

            foreach (var endpoint in _endpoints)
            {
                endpoint.Handler.Dispose();
            }
        }

        private Task SnapshotAsync(bool isCalledFromTimer)
        {
            if (isCalledFromTimer && ShutdownCalled) // don't perform timer actions if we're shutting down
                return Task.CompletedTask;

            try
            {
                if (BeforeSerialization != null && BeforeSerialization.GetInvocationList().Length != 0)
                    BeforeSerialization();

                // snapshot any metric sets
                foreach (var set in _sets)
                {
                    set.Snapshot();
                }

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

        private async Task FlushAsync(bool isCalledFromTimer)
        {
            if (isCalledFromTimer && ShutdownCalled) // don't perform timer actions if we're shutting down
                return;

            if (_endpoints.Length == 0)
            {
                Debug.WriteLine("StackExchange.Metrics: No endpoints. Dropping data.");
                return;
            }

            foreach (var endpoint in _endpoints)
            {
                Debug.WriteLine($"StackExchange.Metrics: Flushing metrics for {endpoint.Name}");

                try
                {
                    await endpoint.Handler.FlushAsync(
                        RetryInterval,
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
                catch (Exception ex)
                {
                    // this will be hit if a sending operation repeatedly fails
                    SendExceptionToHandler(ex);
                }
            }
        }

        private void SerializeMetrics(MetricEndpoint endpoint, DateTime timestamp, out long metricsCount, out long bytesWritten)
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

        private void SerializeMetadata(MetricEndpoint endpoint, IEnumerable<MetaData> metadata)
        {
            Debug.WriteLine("StackExchange.Metrics: Serializing metadata.");
            endpoint.Handler.SerializeMetadata(metadata);
            _lastMetadataFlushTime = DateTime.UtcNow;
            Debug.WriteLine("StackExchange.Metrics: Serialized metadata.");
        }

        private IReadOnlyList<MetaData> GatherMetaData()
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

        private void SendExceptionToHandler(Exception ex)
        {
            if (!ShouldSendException(ex))
                return;

            try
            {
                ExceptionHandler(ex);
            }
            catch (Exception) { } // there's nothing else we can do if the user-supplied exception handler itself throws an exception
        }

        private bool ShouldSendException(Exception ex)
        {
            if (ex is MetricPostException post)
            {
                if (post.SkipExceptionHandler)
                {
                    return false;
                }

                if (ThrowOnPostFail)
                    return true;

                return false;
            }

            if (ex is MetricQueueFullException)
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
