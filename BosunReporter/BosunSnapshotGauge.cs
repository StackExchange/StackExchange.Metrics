using System.Collections.Generic;

namespace BosunReporter
{
    public abstract class BosunSnapshotGauge : BosunMetric
    {
        public override string MetricType
        {
            get { return "gauge"; }
        }

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            var val = GetValue();
            if (!val.HasValue)
                yield break;

            yield return ToJson("", val.Value.ToString("0.###############"), unixTimestamp);
        }

        protected abstract double? GetValue();
    }
}
