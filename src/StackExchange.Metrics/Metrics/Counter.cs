using StackExchange.Metrics.Infrastructure;
using System;
using System.Collections.Immutable;
using System.Threading;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// A general-purpose manually incremented long-integer counter.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#counter
    /// </summary>
    public sealed class Counter : MetricBase
    {
        private long _count;

        /// <summary>
        /// The metric type (counter, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Counter;

        /// <summary>
        /// Instantiates a new counter.
        /// </summary>
        internal Counter(string name, string unit, string description, MetricSourceOptions options, ImmutableDictionary<string, string> tags = null) : base(name, unit, description, options, tags)
        {
        }

        /// <summary>
        /// Increments the counter by <paramref name="amount"/>. This method is thread-safe.
        /// </summary>
        public void Increment(long amount = 1) => Interlocked.Add(ref _count, amount);

        /// <inheritdoc/>
        protected override void Write(IMetricReadingBatch batch, DateTime timestamp)
        {
            var countSnapshot = Interlocked.Exchange(ref _count, 0);
            if (countSnapshot == 0)
            {
                return;
            }

            batch.Add(
                CreateReading(countSnapshot, timestamp)
            );
        }
    }
}
