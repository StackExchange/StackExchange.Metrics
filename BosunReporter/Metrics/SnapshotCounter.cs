using System;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    /// <summary>
    /// Calls a user-provided Func&lt;long?&gt; to get the current counter value each time metrics are going to be posted to the Bosun API.
    /// See https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#snapshotcounter
    /// </summary>
    public class SnapshotCounter : BosunMetric
    {
        public readonly Func<long?> GetCountFunc;

        /// <summary>
        /// The type of metric (counter, in this case).
        /// </summary>
        public override string MetricType => "counter";

        /// <summary>
        /// Initializes a new snapshot counter. The counter will call <paramref name="getCountFunc"/> at each reporting interval in order to get the current
        /// value.
        /// </summary>
        public SnapshotCounter(Func<long?> getCountFunc)
        {
            if (getCountFunc == null)
                throw new ArgumentNullException("getCountFunc");

            GetCountFunc = getCountFunc;
        }

        protected SnapshotCounter()
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
        /// Returns the current value which should be reported for the counter.
        /// </summary>
        protected virtual long? GetValue()
        {
            return GetCountFunc();
        }
    }
}
