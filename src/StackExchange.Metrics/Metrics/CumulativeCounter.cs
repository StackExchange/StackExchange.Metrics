using System;
using System.Collections.Immutable;
using System.Threading;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// A counter that is recorded using the deltas everytime it is incremented . Used for very low-volume events.
    /// </summary>
    /// <remarks>
    /// When using a Bosun endpoint <see cref="BosunMetricHandler.EnableExternalCounters"/> must be true
    /// to be reported. You'll also need to make sure your infrastructure is setup with external counters enabled. This currently requires using tsdbrelay.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#externalcounter
    /// </remarks>
    public sealed class CumulativeCounter : MetricBase
    {
        private long _count;

        /// <summary>
        /// Instantiates a new cumulative counter.
        /// </summary>
        public CumulativeCounter(string name, string unit, string description, MetricSourceOptions options, ImmutableDictionary<string, string> tags = null) : base(name, unit, description, options, tags)
        {
        }

        /// <summary>
        /// The type of metric (cumulative counter, in this case)
        /// </summary>
        public override MetricType MetricType => MetricType.CumulativeCounter;

        /// <inheritdoc/>
        public override void WriteReadings(IMetricReadingBatch batch, DateTime timestamp)
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

        /// <summary>
        /// Increments the counter by one. If you need to increment by more than one at a time, it's probably too high volume for an cumulative counter anyway.
        /// </summary>
        public void Increment() => Interlocked.Increment(ref _count);
    }
}
