using System;
using System.Collections.Generic;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    public class SnapshotCounter : BosunMetric
    {
        public readonly Func<long?> GetCountFunc;

        public override string MetricType => "counter";

        public SnapshotCounter(Func<long?> getCountFunc)
        {
            if (getCountFunc == null)
                throw new ArgumentNullException("getCountFunc");

            GetCountFunc = getCountFunc;
        }

        protected SnapshotCounter()
        {
        }

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            var val = GetValue();
            if (!val.HasValue)
                yield break;

            yield return ToJson("", val.Value, unixTimestamp);
        }

        protected virtual long? GetValue()
        {
            return GetCountFunc();
        }
    }
}
