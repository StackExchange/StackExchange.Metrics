using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BosunReporter.Infrastructure;
using System;

namespace BosunReporter.Metrics
{
    public class EventGauge : BosunMetric
    {
        private struct PendingMetric
        {
            public double Value;
            public DateTime Time;
        }

        private ConcurrentBag<PendingMetric> _pendingMetrics = new ConcurrentBag<PendingMetric>();

        public override string MetricType => "gauge";

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

        public void Record(double value)
        {
            AssertAttached();
            _pendingMetrics.Add(new PendingMetric { Value = value, Time = DateTime.UtcNow });
        }

        public void Record(double value, DateTime time)
        {
            AssertAttached();
            _pendingMetrics.Add(new PendingMetric { Value = value, Time = time });
        }
    }
}
