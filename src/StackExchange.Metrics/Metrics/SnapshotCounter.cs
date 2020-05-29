using System;
using System.Collections.Immutable;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Calls a user-provided Func&lt;long?&gt; to get the current counter value each time metrics are going to be posted to a metrics handler.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#snapshotcounter
    /// </summary>
    public sealed class SnapshotCounter : MetricBase
    {
        private readonly Func<long?> _getCountFunc;

        /// <summary>
        /// The type of metric (counter, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Counter;

        /// <summary>
        /// Initializes a new snapshot counter. The counter will call <paramref name="getCountFunc"/> at each reporting interval in order to get the current
        /// value.
        /// </summary>
        internal SnapshotCounter(Func<long?> getCountFunc, string name, string unit, string description, MetricSourceOptions options, ImmutableDictionary<string, string> tags = null) : base(name, unit, description, options, tags)
        {
            _getCountFunc = getCountFunc ?? throw new ArgumentNullException("getCountFunc");
        }

        /// <inheritdoc/>
        public override void WriteReadings(IMetricReadingBatch batch, DateTime timestamp)
        {
            var val = _getCountFunc();
            if (!val.HasValue || val.Value == 0)
            {
                return;
            }

            batch.Add(
                CreateReading(val.Value, timestamp)
            );
        }
    }
}
