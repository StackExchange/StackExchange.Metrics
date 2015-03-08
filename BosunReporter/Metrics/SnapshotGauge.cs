using System;
using System.Collections.Generic;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    public class SnapshotGauge : BosunMetric
    {
        public readonly Func<double?> GetValueFunc;

        public override string MetricType => "gauge";

        public SnapshotGauge(Func<double?> getValueFunc)
        {
            if (getValueFunc == null)
                throw new ArgumentNullException("getValueFunc");

            GetValueFunc = getValueFunc;
        }

        protected SnapshotGauge()
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
