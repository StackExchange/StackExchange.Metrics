using StackExchange.Metrics.Infrastructure;
using System;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Calls a user-provided Func&lt;long?&gt; to get the current counter value each time metrics are going to be posted to a metrics handler.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#snapshotcounter
    /// </summary>
    public class SnapshotCounter : MetricBase
    {
        readonly Func<long?> _getCountFunc;

        /// <summary>
        /// The type of metric (counter, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Counter;

        /// <summary>
        /// Initializes a new snapshot counter. The counter will call <paramref name="getCountFunc"/> at each reporting interval in order to get the current
        /// value.
        /// </summary>
        public SnapshotCounter(Func<long?> getCountFunc)
        {
            if (getCountFunc == null)
                throw new ArgumentNullException("getCountFunc");

            _getCountFunc = getCountFunc;
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
        /// Returns the current value which should be reported for the counter.
        /// </summary>
        public virtual long? GetValue()
        {
            return _getCountFunc();
        }
    }
}
