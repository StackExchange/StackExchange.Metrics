#if !NETCOREAPP
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="MetricSource" /> to provide GC metrics:
    ///  - Gen0 collections
    ///  - Gen1 collections
    ///  - Gen2 collections
    /// </summary>
    public sealed class GarbageCollectorMetricSource : MetricSource
    {
        private SamplingGauge _gen0;
        private SamplingGauge _gen1;
        private SamplingGauge _gen2;

        /// <summary>
        /// Constructs a new instance of <see cref="GarbageCollectorMetricSource" />.
        /// </summary>
        public GarbageCollectorMetricSource(MetricSourceOptions options) : base(options)
        {
            _gen0 = AddSamplingGauge("dotnet.mem.collections.gen0", "collections", "Number of gen-0 collections");
            _gen1 = AddSamplingGauge("dotnet.mem.collections.gen1", "collections", "Number of gen-1 collections");
            _gen2 = AddSamplingGauge("dotnet.mem.collections.gen2", "collections", "Number of gen-2 collections");
        }

        /// <inheritdoc/>
        public override void Attach(IMetricsCollector collector)
        {
            collector.BeforeSerialization += Snapshot;
        }

        /// <inheritdoc/>
        public override void Detach(IMetricsCollector collector)
        {
            collector.BeforeSerialization -= Snapshot;
        }

        private void Snapshot()
        {
            _gen0.Record(GC.CollectionCount(0));
            _gen1.Record(GC.CollectionCount(1));
            _gen2.Record(GC.CollectionCount(2));
        }
    }
}
#endif
