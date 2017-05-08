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
        private struct PendingMetric
        {
            public double Value;
            public DateTime Time;
        }

        private ConcurrentBag<PendingMetric> _pendingMetrics = new ConcurrentBag<PendingMetric>();

        /// <summary>
        /// The type of metric (gauge).
        /// </summary>
        public override string MetricType => "gauge";

        /// <summary>
        /// See <see cref="BosunMetric.Serialize"/>
        /// </summary>
        protected override void Serialize(MetricWriter writer, DateTime now)
        {
            if (_pendingMetrics.Count == 0)
                return;

            var pending = Interlocked.Exchange(ref _pendingMetrics, new ConcurrentBag<PendingMetric>());
            foreach (var p in pending)
            {
                WriteValue(writer, p.Value, p.Time);
            }
        }

        /// <summary>
        /// Records a data point which will be sent to Bosun.
        /// </summary>
        public void Record(double value)
        {
            AssertAttached();
            _pendingMetrics.Add(new PendingMetric { Value = value, Time = DateTime.UtcNow });
        }

        /// <summary>
        /// Records a data point with an explicit timestamp which will be sent to Bosun.
        /// </summary>
        public void Record(double value, DateTime time)
        {
            AssertAttached();
            _pendingMetrics.Add(new PendingMetric { Value = value, Time = time });
        }
    }
}
