using System;
using System.Threading;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    public class Counter : BosunMetric
    {
        public long Value = 0;

        public override string MetricType => "counter";

        protected override void Serialize(MetricWriter writer, DateTime now)
        {
            WriteValue(writer, Value, now);
        }

        public Counter()
        {
        }

        public void Increment(long amount = 1)
        {
            AssertAttached();
            Interlocked.Add(ref Value, amount);
        }
    }
}
