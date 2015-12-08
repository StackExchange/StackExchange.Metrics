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

        protected override void Serialize(MetricWriter writer, string unixTimestamp)
        {
            var val = GetValue();
            if (!val.HasValue)
                return;

            WriteValue(writer, val.Value, unixTimestamp);
        }

        protected virtual double? GetValue()
        {
            return GetValueFunc();
        }
    }
}
