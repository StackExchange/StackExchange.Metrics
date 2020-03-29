#if !NETCOREAPP
using System;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="IMetricSet" /> to provide GC metrics:
    ///  - Gen0 collections
    ///  - Gen1 collections
    ///  - Gen2 collections
    /// </summary>
    public sealed class GarbageCollectorMetricSet : IMetricSet
    {
        private SamplingGauge _gen0;
        private SamplingGauge _gen1;
        private SamplingGauge _gen2;

        /// <summary>
        /// Constructs a new instance of <see cref="GarbageCollectorMetricSet" />.
        /// </summary>
        public GarbageCollectorMetricSet() { }

        /// <inheritdoc/>
        public void Initialize(IMetricsCollector collector)
        {
            _gen0 = collector.CreateMetric<SamplingGauge>("dotnet.mem.collections.gen0", "collections", "Number of gen-0 collections", includePrefix: false);
            _gen1 = collector.CreateMetric<SamplingGauge>("dotnet.mem.collections.gen1", "collections", "Number of gen-1 collections", includePrefix: false);
            _gen2 = collector.CreateMetric<SamplingGauge>("dotnet.mem.collections.gen2", "collections", "Number of gen-2 collections", includePrefix: false);
        }

        /// <inheritdoc/>
        public void Snapshot()
        {
            _gen0.Record(GC.CollectionCount(0));
            _gen1.Record(GC.CollectionCount(1));
            _gen2.Record(GC.CollectionCount(2));
        }
    }
}
#endif
