using StackExchange.Metrics.Infrastructure;
using System;
using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Exception uses when posting to a metric handler fails.
    /// </summary>
    [SuppressMessage("Design", "RCS1194:Implement exception constructors.", Justification = "Only ever constructed in specific code paths, other ctors unnecessary")]
    public class MetricPostException : Exception
    {
        internal MetricPostException(Exception innerException)
            : base("Posting to the metrics endpoint failed.", innerException)
        {
        }

        /// <summary>
        /// Gets or sets a value indicating
        /// </summary>
        internal bool SkipExceptionHandler { get; set; }
    }


    /// <summary>
    /// Exception used when a metric queue is full and payloads are being dropped. This typically happens after repeated failures to post to the API.
    /// </summary>
    [SuppressMessage("Design", "RCS1194:Implement exception constructors.", Justification = "Only ever constructed in specific code paths, other ctors unnecessary")]
    public class MetricQueueFullException : Exception
    {
        /// <summary>
        /// The number of data points which were lost.
        /// </summary>
        public int MetricsCount { get; }

        internal MetricQueueFullException(PayloadType payloadType, int metricsCount)
            : base($"{payloadType} metric queue is full. Metric data is likely being lost due to repeated failures in posting to the endpoint API.")
        {
            MetricsCount = metricsCount;

            Data["MetricsCount"] = metricsCount;
        }
    }
}
