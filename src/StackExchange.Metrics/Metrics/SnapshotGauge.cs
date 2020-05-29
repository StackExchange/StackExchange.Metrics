using System;
using System.Collections.Immutable;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Similar to a SnapshotCounter, it calls a user provided Func&lt;double?&gt; to get the current gauge value each time metrics are going to be posted to
    /// a metrics handler. See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#snapshotgauge
    /// </summary>
    public sealed class SnapshotGauge : MetricBase
    {
        private readonly Func<double?> _getValueFunc;

        /// <summary>
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <summary>
        /// Initializes a new snapshot gauge. The counter will call <paramref name="getValueFunc"/> at each reporting interval in order to get the current
        /// value.
        /// </summary>
        internal SnapshotGauge(Func<double?> getValueFunc, string name, string unit, string description, MetricSourceOptions options, ImmutableDictionary<string, string> tags = null) : base(name, unit, description, options, tags)
        {
            _getValueFunc = getValueFunc ?? throw new ArgumentNullException("getValueFunc");
        }

        /// <inheritdoc/>
        protected override void WriteReadings(IMetricReadingBatch batch, DateTime timestamp)
        {
            var val = _getValueFunc();
            if (!val.HasValue || double.IsNaN(val.Value))
            {
                return;
            }

            batch.Add(
                CreateReading(val.Value, timestamp)
            );
        }
    }
}
