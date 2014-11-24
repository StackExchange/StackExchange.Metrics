using System.Collections.Generic;
using System.Threading;

namespace BosunReporter
{
    public abstract class BosunCounter : BosunMetric
    {
        public long Value;

        private readonly object _tagsLock = new object();
        private string _tags;

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            if (_tags == null)
            {
                lock (_tagsLock)
                {
                    if (_tags == null)
                    {
                        _tags = SerializeTags();
                    }
                }
            }

            yield return ToJson(Value.ToString("D"), _tags, unixTimestamp);
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
