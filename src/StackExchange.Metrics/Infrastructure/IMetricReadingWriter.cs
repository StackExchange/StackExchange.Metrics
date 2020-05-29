using System;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Exposes a way to write <see cref="MetricReading"/> from a metric or metric source.
    /// </summary>
    internal interface IMetricReadingWriter
    {
        /// <summary>
        /// Writes the readings for a metric into the specified <see cref="IMetricReadingBatch"/>.
        /// </summary>
        /// <param name="batch">
        /// <see cref="IMetricReadingBatch"/> to write metrics into.
        /// </param>
        /// <param name="timestamp">
        /// Timestamp applied to all readings.
        /// </param>
        void WriteReadings(IMetricReadingBatch batch, DateTime timestamp);
    }
}
