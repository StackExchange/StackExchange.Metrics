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
        /// <summary>
        /// The underlying field for <see cref="Value"/>. This allows for direct manipulation via Interlocked methods.
        /// </summary>
        protected long _value;

        /// <summary>
        /// The current value of the counter.
        /// </summary>
        public long Value => _value;

        /// <summary>
        /// The metric type (counter, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Counter;

        /// <summary>
        /// Serializes the counter.
        /// </summary>
        protected override void Serialize(IMetricBatch writer, DateTime now)
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
            Interlocked.Add(ref _value, amount);
        }
    }
}
