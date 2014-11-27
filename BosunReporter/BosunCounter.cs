using System.Collections.Generic;
using System.Threading;

namespace BosunReporter
{
    public abstract class BosunCounter : BosunMetric
    {
        public long Value;

        private readonly object _tagsLock = new object();

        public override string MetricType
        {
            get { return "counter"; }
        }

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            yield return ToJson("", Value.ToString("D"), unixTimestamp);
        }

        protected BosunCounter(long value = 0)
        {
            Value = value;
        }

        public void Increment(long amount = 1)
        {
            Interlocked.Add(ref Value, amount);
        }
    }
}
