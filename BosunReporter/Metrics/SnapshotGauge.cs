using System;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    /// <summary>
    /// Similar to a SnapshotCounter, it calls a user provided Func&lt;double?&gt; to get the current gauge value each time metrics are going to be posted to
    /// the Bosun API. See https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#snapshotgauge
    /// </summary>
    public class SnapshotGauge : BosunMetric
    {
        public readonly Func<double?> GetValueFunc;

        /// <summary>
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override string MetricType => "gauge";

        /// <summary>
        /// Initializes a new snapshot gauge. The counter will call <paramref name="getValueFunc"/> at each reporting interval in order to get the current
        /// value.
        /// </summary>
        public SnapshotGauge(Func<double?> getValueFunc)
        {
            if (getValueFunc == null)
                throw new ArgumentNullException("getValueFunc");

            GetValueFunc = getValueFunc;
        }

        protected SnapshotGauge()
        {
        }

        /// <summary>
        /// See <see cref="BosunMetric.Serialize"/>
        /// </summary>
        protected override void Serialize(MetricWriter writer, DateTime now)
        {
            var val = GetValue();
            if (!val.HasValue)
                return;

            WriteValue(writer, val.Value, now);
        }

        /// <summary>
        /// Returns the current value which should be reported for the gauge.
        /// </summary>
        protected virtual double? GetValue()
        {
            return GetValueFunc();
        }
    }
}
