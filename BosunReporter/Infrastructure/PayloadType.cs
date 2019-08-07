namespace BosunReporter.Infrastructure
{
    /// <summary>
    /// Type of payload to send.
    /// </summary>
    public enum PayloadType
    {
        /// <summary>
        /// A cumulative counter metric.
        /// </summary>
        CumulativeCounter = 0,
        /// <summary>
        /// A counter metric.
        /// </summary>
        Counter = 1,
        /// <summary>
        /// A gauge metric.
        /// </summary>
        Gauge = 2,
        /// <summary>
        /// Metadata about metrics.
        /// </summary>
        Metadata = 3,
    }
}