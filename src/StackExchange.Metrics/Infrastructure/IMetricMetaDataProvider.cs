using System.Collections.Generic;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Exposes a way to read <see cref="Metadata"/> from a metric.
    /// </summary>
    public interface IMetricMetadataProvider
    {
        /// <summary>
        /// Returns an enumerable of <see cref="Metadata"/> which describes a metric.
        /// </summary>
        IEnumerable<Metadata> GetMetadata();
    }
}
