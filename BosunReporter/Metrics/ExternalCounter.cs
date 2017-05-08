using System;
using System.Threading;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    /// <summary>
    /// A persistent counter (no resets) for very low-volume events. <see cref="MetricsCollector.EnableExternalCounters"/> must be true for external counters
    /// to be reported. You can set the initial state via <see cref="BosunOptions.EnableExternalCounters"/>. You'll also need to make sure your infrastructure
    /// is setup with external counters enabled. This currently requires using tsdbrelay.
    /// See https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#externalcounter
    /// </summary>
    [ExcludeDefaultTags("host")]
    public class ExternalCounter : BosunMetric
    {
        private int _count;

        /// <summary>
        /// The current value of this counter. This will reset to zero at each reporting interval.
        /// </summary>
        public int Count => _count;
        /// <summary>
        /// The type of metric (counter, in this case)
        /// </summary>
        public override string MetricType => "counter";

        /// <summary>
        /// Increments the counter by one. If you need to increment by more than one at a time, it's probably too high volume for an external counter anyway.
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref _count);
        }

        /// <summary>
        /// See <see cref="BosunMetric.Serialize"/>
        /// </summary>
        protected override void Serialize(MetricWriter writer, DateTime now)
        {
            var increment = Interlocked.Exchange(ref _count, 0);
            if (increment == 0)
                return;

            WriteValue(writer, increment, now);
        }
    }
}