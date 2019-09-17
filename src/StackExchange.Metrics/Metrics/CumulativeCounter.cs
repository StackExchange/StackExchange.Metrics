using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;
using System;
using System.Threading;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// A counter that is recorded using the deltas everytime it is incremented . Used for very low-volume events.
    /// <remarks>
    /// When using a Bosun endpoint <see cref="BosunMetricHandler.EnableExternalCounters"/> must be true
    /// to be reported. You'll also need to make sure your infrastructure is setup with external counters enabled. This currently requires using tsdbrelay.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#externalcounter
    /// </remarks>
    /// </summary>
    public class CumulativeCounter : MetricBase
    {
        long _countSnapshot;
        long _count;

        /// <summary>
        /// The current value of this counter. This will reset to zero at each reporting interval.
        /// </summary>
        public long Count => _count;

        /// <summary>
        /// The type of metric (cumulative counter, in this case)
        /// </summary>
        public override MetricType MetricType => MetricType.CumulativeCounter;

        /// <summary>
        /// Increments the counter by one. If you need to increment by more than one at a time, it's probably too high volume for an external counter anyway.
        /// </summary>
        public void Increment()
        {
            AssertAttached();
            Interlocked.Increment(ref _count);
        }

        /// <summary>
        /// See <see cref="MetricBase.Serialize"/>
        /// </summary>
        protected override void Serialize(IMetricBatch writer, DateTime now)
        {
            if (_countSnapshot > 0)
            {
                WriteValue(writer, _countSnapshot, now);
            }
        }

        /// <summary>
        /// See <see cref="MetricBase.PreSerialize"/>
        /// </summary>
        protected override void PreSerialize()
        {
            _countSnapshot = Interlocked.Exchange(ref _count, 0);
        }
    }
}
