using System.Collections.Concurrent;
using System.Threading;
using BosunReporter.Infrastructure;
using System;

namespace BosunReporter.Metrics
{
    /// <summary>
    /// Every data point is sent to Bosun. Good for low-volume events.
    /// See https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#eventgauge
    /// </summary>
    public class EventGauge : BosunMetric
    {
        readonly struct PendingMetric
        {
            public PendingMetric(double value, DateTime time)
            {
                Value = value;
                Time = time;
            }

            public double Value { get; }
            public DateTime Time { get; }
        }

        ConcurrentBag<PendingMetric> _pendingSnapshot;
        ConcurrentBag<PendingMetric> _pendingMetrics = new ConcurrentBag<PendingMetric>();

        /// <summary>
        /// The type of metric (gauge).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <summary>
        /// See <see cref="BosunMetric.Serialize"/>
        /// </summary>
        protected override void Serialize(IMetricBatch writer, DateTime now)
        {
            var pending = _pendingSnapshot;
            if (pending.Count == 0)
                return;
            
            foreach (var p in pending)
            {
                WriteValue(writer, p.Value, p.Time);
            }
        }

        /// <summary>
        /// See <see cref="BosunMetric.PreSerialize"/>
        /// </summary>
        protected override void PreSerialize()
        {
            _pendingSnapshot = Interlocked.Exchange(ref _pendingMetrics, new ConcurrentBag<PendingMetric>());
        }

        /// <summary>
        /// Records a data point which will be sent to Bosun.
        /// </summary>
        public void Record(double value)
        {
            AssertAttached();
            _pendingMetrics.Add(new PendingMetric(value, DateTime.UtcNow));
        }

        /// <summary>
        /// Records a data point with an explicit timestamp which will be sent to Bosun.
        /// </summary>
        public void Record(double value, DateTime time)
        {
            AssertAttached();
            _pendingMetrics.Add(new PendingMetric(value, time));
        }
    }
}
