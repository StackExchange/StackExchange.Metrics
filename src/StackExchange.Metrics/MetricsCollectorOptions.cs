using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics
{
    /// <summary>
    /// A delegate signature for globally modifying tag values. See <see cref="MetricsCollectorOptions.TagValueConverter"/>.
    /// </summary>
    public delegate string TagValueConverterDelegate(string tagName, string tagValue);

    /// <summary>
    /// Defines initialization options for <see cref="MetricsCollector"/>.
    /// </summary>
    public class MetricsCollectorOptions
    {
        /// <summary>
        /// Exceptions which occur on a background thread will be passed to this delegate.
        /// </summary>
        public Action<Exception> ExceptionHandler { get; set; } = _ => { };
        /// <summary>
        /// If provided, all metric names will be prefixed with this value. This gives you the ability to keyspace your application. For example, you might
        /// want to use something like "app1.".
        /// </summary>
        public string MetricsNamePrefix { get; set; }
        /// <summary>
        /// Zero or more endpoints to publish metrics to. If this is empty, metrics will be discarded instead of sent to any endpoints.
        /// </summary>
        public IEnumerable<MetricEndpoint> Endpoints { get; set; } = Enumerable.Empty<MetricEndpoint>();
        /// <summary>
        /// Zero or more pre-packaged sets of metrics. These are snapshotted every <see cref="SnapshotInterval"/>.
        /// </summary>
        public IEnumerable<IMetricSet> Sets { get; set; } = Enumerable.Empty<IMetricSet>();
        /// <summary>
        /// If true, <see cref="MetricsCollector" /> will generate an exception every time posting to an endpoint fails with a server error (response code 5xx).
        /// </summary>
        public bool ThrowOnPostFail { get; set; } = false;
        /// <summary>
        /// If true, <see cref="MetricsCollector" /> will generate an exception when the metric queue is full. This would most commonly be caused by an extended outage of the
        /// Bosun API. It is an indication that data is likely being lost.
        /// </summary>
        public bool ThrowOnQueueFull { get; set; } = true;
        /// <summary>
        /// The length of time between metric reports (snapshots). Defaults to 30 seconds.
        /// </summary>
        public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>
        /// The length of time between flushing metrics to an endpoint. Defaults to 1 seconds.
        /// </summary>
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The length of time to wait when flushing metrics to an endpoint fails. Defaults to 5 seconds.
        /// </summary>
        public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Allows you to specify a function which takes a property name and returns a tag name. This may be useful if you want to convert PropertyName to
        /// property_name or similar transformations. This function does not apply to any tag names which are set manually via the <see cref="MetricTag" /> attribute.
        /// </summary>
        public Func<string, string> PropertyToTagName { get; set; }
        /// <summary>
        /// Allows you to specify a function which takes a tag name and value, and returns a possibly altered value. This could be used as a global sanitizer
        /// or normalizer. It is applied to all tag values, including default tags. If the return value is not a valid tag, an exception will be
        /// thrown. Null values are possible for the tagValue argument, so be sure to handle nulls appropriately.
        /// </summary>
        public TagValueConverterDelegate TagValueConverter { get; set; }
        /// <summary>
        /// A list of tag names/values which will be automatically inculuded on every metric. The IgnoreDefaultTags attribute can be used on classes inheriting
        /// from <see cref="MetricBase" /> to exclude default tags. If an inherited class has a conflicting BosunTag field, it will override the default tag value. Default
        /// tags will generally not be included in metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> DefaultTags { get; set; } = ImmutableDictionary<string, string>.Empty;

        /// <summary>
        /// Defines initialization options for <see cref="MetricsCollector"/>.
        /// All options are optional. However, <see cref="MetricsCollector" /> will never send metrics unless <see cref="Endpoints"/> is set.
        /// </summary>
        public MetricsCollectorOptions()
        {
        }
    }
}
