namespace BosunReporter.Infrastructure
{
    /// <summary>
    /// Types of metric the framework can handle.
    /// </summary>
    public enum MetricType
    {
        /// <summary>
        /// A counter.
        /// </summary>
        Counter,
        /// <summary>
        /// A cumulative counter.
        /// </summary>
        CumulativeCounter,
        /// <summary>
        /// A gauge.
        /// </summary>
        Gauge,
    }
}
