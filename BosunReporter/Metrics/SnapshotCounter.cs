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

        protected override void Serialize(MetricWriter writer, string unixTimestamp)
        {
            var val = GetValue();
            if (!val.HasValue)
                return;

            WriteValue(writer, val.Value, unixTimestamp);
        }

        protected virtual long? GetValue()
        {
            return GetCountFunc();
        }
    }
}
