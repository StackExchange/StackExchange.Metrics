using System;
using System.Threading;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    /// <summary>
    /// A general-purpose manually incremented long-integer counter.
    /// See https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#counter
    /// </summary>
    public class Counter : BosunMetric
    {
        public long Value = 0;

        /// <summary>
        /// The metric type (counter, in this case).
        /// </summary>
        public override string MetricType => "counter";

        /// <summary>
        /// Serializes the counter.
        /// </summary>
        protected override void Serialize(MetricWriter writer, DateTime now)
        {
            WriteValue(writer, Value, now);
        }

        /// <summary>
        /// Instantiates a new counter. You should typically use a method on <see cref="MetricsCollector"/>, such as CreateMetric, instead of instantiating
        /// directly via this constructor.
        /// </summary>
        public Counter()
        {
        }

        /// <summary>
        /// Increments the counter by <paramref name="amount"/>. This method is thread-safe.
        /// </summary>
        public void Increment(long amount = 1)
        {
            AssertAttached();
            Interlocked.Add(ref Value, amount);
        }
    }
}
