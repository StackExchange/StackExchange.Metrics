using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Every data point results in a <see cref="MetricReading"/>. Good for low-volume events.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#eventgauge
    /// </summary>
    public sealed class EventGauge : MetricBase
    {
        private ConcurrentBag<PendingMetric> _pendingMetrics = new ConcurrentBag<PendingMetric>();

        /// <summary>
        /// Instantiates a new event gauge.
        /// </summary>
        internal EventGauge(string name, string unit, string description, MetricSourceOptions options, ImmutableDictionary<string, string> tags = null) : base(name, unit, description, options, tags)
        {
        }

        /// <summary>
        /// The type of metric (gauge).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <summary>
        /// Records a data point which will be sent to metrics handlers.
        /// </summary>
        public void Record(double value) => _pendingMetrics.Add(new PendingMetric(value, DateTime.UtcNow));

        /// <summary>
        /// Records a data point with an explicit timestamp which will be sent to metrics handlers.
        /// </summary>
        public void Record(double value, DateTime time) => _pendingMetrics.Add(new PendingMetric(value, time));

        /// <inheritdoc/>
        public override void WriteReadings(IMetricReadingBatch batch, DateTime timestamp)
        {
            var pending = Interlocked.Exchange(ref _pendingMetrics, new ConcurrentBag<PendingMetric>());
            if (pending == null || pending.Count == 0)
            {
                return;
            }

            foreach (var p in pending)
            {
                batch.Add(
                    CreateReading(p.Value, p.Time)
                );
            }
        }

        private readonly struct PendingMetric
        {
            public PendingMetric(double value, DateTime time)
            {
                Value = value;
                Time = time;
            }

            public double Value { get; }
            public DateTime Time { get; }
        }
    }
}
