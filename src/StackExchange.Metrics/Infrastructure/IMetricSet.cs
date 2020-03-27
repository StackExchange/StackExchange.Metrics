namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Represents a pre-packaged set of metrics that are snapshotted
    /// to a <see cref="MetricsCollector" /> once per <see cref="MetricsCollectorOptions.SnapshotInterval" />.
    /// </summary>
    public interface IMetricSet
    {
        /// <summary>
        /// Initializes the set by creating metrics using the passed <see cref="IMetricsCollector" />.
        /// </summary>
        void Initialize(IMetricsCollector collector);

        /// <summary>
        /// Snapshots the metrics in this set.
        /// </summary>
        void Snapshot();
    }
}
