using StackExchange.Metrics.Infrastructure;
using System;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Similar to a SnapshotCounter, it calls a user provided Func&lt;double?&gt; to get the current gauge value each time metrics are going to be posted to
    /// the Bosun API. See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#snapshotgauge
    /// </summary>
    public class SnapshotGauge : MetricBase
    {
        readonly Func<double?> _getValueFunc;

        /// <summary>
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <summary>
        /// Initializes a new snapshot gauge. The counter will call <paramref name="getValueFunc"/> at each reporting interval in order to get the current
        /// value.
        /// </summary>
        public SnapshotGauge(Func<double?> getValueFunc)
        {
            if (getValueFunc == null)
                throw new ArgumentNullException("getValueFunc");

            _getValueFunc = getValueFunc;
        }

        /// <summary>
        /// See <see cref="MetricBase.Serialize"/>
        /// </summary>
        protected override void Serialize(IMetricBatch writer, DateTime now)
        {
            var val = GetValue();
            if (!val.HasValue)
                return;

            WriteValue(writer, val.Value, now);
        }

        /// <summary>
        /// Returns the current value which should be reported for the gauge.
        /// </summary>
        public virtual double? GetValue()
        {
            return _getValueFunc();
        }
    }
}
