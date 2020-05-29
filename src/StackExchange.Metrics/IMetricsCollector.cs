using System;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Exposes functionality to create new metrics and to collect metrics.
    /// </summary>
    public interface IMetricsCollector
    {
        /// <summary>
        /// An event called immediately before metrics are serialized.
        /// </summary>
        event Action BeforeSerialization;
        /// <summary>
        /// An event called immediately after metrics are serialized. It includes an argument with post-serialization information.
        /// </summary>
        event Action<AfterSerializationInfo> AfterSerialization;
        /// <summary>
        /// An event called immediately after metrics are posted to a metric handler. It includes an argument with information about the POST.
        /// </summary>
        event Action<AfterSendInfo> AfterSend;

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
        /// Number of times to retry a flush operation before giving up.
        /// </summary>
        public int RetryCount { get; }
        /// <summary>
        /// The length of time to wait before retrying a failed flush operation to an endpoint.
        /// </summary>
        public TimeSpan RetryInterval { get; }
    }
}
