namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Exposes functionality used to add metric readings to a batch that will be
    /// serialized and later flushed to a metric endpoint.
    /// </summary>
    public interface IMetricReadingBatch
    {
        /// <summary>
        /// Number of bytes written in this batch.
        /// </summary>
        long BytesWritten { get; }

        /// <summary>
        /// Number of metric readings written in this batch.
        /// </summary>
        long MetricsWritten { get; }

        /// <summary>
        /// Adds a metric reading to the batch in preparation for flushing it to an endpoint.
        /// </summary>
        /// <param name="reading">
        /// <see cref="MetricReading" /> struct containing data about the metric reading to add.
        /// </param>
        void Add(in MetricReading reading);
    }
}
