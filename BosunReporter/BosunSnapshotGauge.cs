using System;
using System.Collections.Generic;

namespace BosunReporter
{
    public class BosunSnapshotGauge : BosunMetric
    {
        public readonly Func<double?> GetValueFunc;

        public override string MetricType => "gauge";

        public BosunSnapshotGauge(Func<double?> getValueFunc)
        {
            if (getValueFunc == null)
                throw new ArgumentNullException("getValueFunc");

            GetValueFunc = getValueFunc;
        }

        protected BosunSnapshotGauge()
        {
        }

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            var val = GetValue();
            if (!val.HasValue)
                yield break;

            yield return ToJson("", val.Value, unixTimestamp);
        }

        protected virtual double? GetValue()
        {
            return GetValueFunc();
        }
    }
}
