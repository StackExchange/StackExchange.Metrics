using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    public class EventGauge : BosunMetric, IDoubleGauge
    {
        private ConcurrentBag<string> _serializedMetrics = new ConcurrentBag<string>();

        public override string MetricType => "gauge";

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            if (_serializedMetrics.Count > 0)
            {
                return Interlocked.Exchange(ref _serializedMetrics, new ConcurrentBag<string>());
            }

            return Enumerable.Empty<string>();
        }

        public void Record(double value)
        {
            AssertAttached();
            _serializedMetrics.Add(ToJson("", value, MetricsCollector.GetUnixTimestamp()));
        }
    }
}
