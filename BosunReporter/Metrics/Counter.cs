using System;
using System.Collections.Generic;
using System.Threading;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    public class Counter : BosunMetric, ILongCounter
    {
        public long Value = 0;

        public override string MetricType => "counter";

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            yield return ToJson("", Value, unixTimestamp);
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
