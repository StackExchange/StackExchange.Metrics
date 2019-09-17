using System;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Exposes functionality used to add metrics to a queue.
    /// </summary>
    public interface IMetricBatch : IDisposable
    {
        /// <summary>
        /// Number of bytes written in this batch.
        /// </summary>
        long BytesWritten { get; }

        /// <summary>
        /// Number of metrics written in this batch.
        /// </summary>
        long MetricsWritten { get; }

        /// <summary>
        /// Serializes a metric in preparation for flushing it to an endpoint.
        /// </summary>
        /// <param name="reading">
        /// <see cref="MetricReading" /> struct containing data about the metric to write.
        /// </param>
        void SerializeMetric(in MetricReading reading);
    }
}