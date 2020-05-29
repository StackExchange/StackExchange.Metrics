using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Defines initialization options for <see cref="MetricsCollector"/>.
    /// </summary>
    /// <remarks>
    /// All options are optional. However, <see cref="MetricsCollector" /> will never send metrics unless <see cref="Endpoints"/> is set.
    /// </remarks>
    public class MetricsCollectorOptions : MetricSourceOptions, IOptions<MetricsCollectorOptions>
    {
        /// <summary>
        /// Exceptions which occur on a background thread will be passed to this delegate.
        /// </summary>
        public Action<Exception> ExceptionHandler { get; set; } = _ => { };
        /// <summary>
        /// Zero or more endpoints to publish metrics to. If this is empty, metrics will be discarded instead of sent to any endpoints.
        /// </summary>
        public IEnumerable<MetricEndpoint> Endpoints { get; set; } = Enumerable.Empty<MetricEndpoint>();
        /// <summary>
        /// Zero or more sources of metrics. Metrics provided by a source are snapshotted every <see cref="SnapshotInterval"/>.
        /// </summary>
        public IEnumerable<MetricSource> Sources { get; set; } = Enumerable.Empty<MetricSource>();
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
        /// Number of times to retry when flushing metrics to an endpoint fails. Defaults to 3 attempts.
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// For easy usage without <see cref="Options.Create{TOptions}(TOptions)"/>.
        /// </summary>
        MetricsCollectorOptions IOptions<MetricsCollectorOptions>.Value => this;
    }
}
