using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Helpful extensions for metrics.
    /// </summary>
    public static class MetricExtensions
    {
        /// <summary>
        /// Writes all readings for all metrics in specified enumerable of <see cref="MetricSource"/>.
        /// </summary>
        public static void WriteReadings(this IEnumerable<MetricSource> sources, IMetricReadingBatch batch, DateTime timestamp)
        {
            foreach (var source in sources)
            {
                source.WriteReadings(batch, timestamp);
            }
        }

        /// <summary>
        /// Gets all readings for all metrics in the specified enumerable of <see cref="MetricSource"/>.
        /// </summary>
        public static ImmutableArray<MetricReading> GetReadings(this IEnumerable<MetricSource> sources, DateTime timestamp)
        {
            var batch = new ArrayBatch();
            sources.WriteReadings(batch, timestamp);
            return batch.ToImmutableArray();
        }

        /// <summary>
        /// Gets readings for associated with a metric.
        /// </summary>
        public static ImmutableArray<MetricReading> GetReadings(this IMetricReadingWriter metric, DateTime timestamp)
        {
            var batch = new ArrayBatch();
            metric.WriteReadings(batch, timestamp);
            return batch.ToImmutableArray();
        }

        /// <summary>
        /// Gets all metadata for all metrics in the specified enumerable of <see cref="MetricSource"/>.
        /// </summary>
        internal static PooledList<Metadata> GetMetadata(this IEnumerable<MetricSource> sources)
        {
            var metadata = new PooledList<Metadata>();
            foreach (var source in sources)
            {
                metadata.AddRange(source.GetMetadata());
            }

            return metadata;
        }

        private class ArrayBatch : IMetricReadingBatch
        {
            private readonly ImmutableArray<MetricReading>.Builder _readings;

            public ArrayBatch()
            {
                _readings = ImmutableArray.CreateBuilder<MetricReading>();
            }

            public long BytesWritten => 0;

            public long MetricsWritten => 0;

            public void Add(in MetricReading reading) => _readings.Add(reading);

            public ImmutableArray<MetricReading> ToImmutableArray() => _readings.ToImmutable();
        }
    }
}
