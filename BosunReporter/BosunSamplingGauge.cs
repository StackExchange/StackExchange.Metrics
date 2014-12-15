using System;
using System.Collections.Generic;
using System.Threading;

namespace BosunReporter
{
    public class BosunSamplingGauge : BosunMetric
    {
        private double _value = Double.NaN;

        public double CurrentValue
        {
            get { return _value; }
        }

        public override string MetricType
        {
            get { return "gauge"; }
        }

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            var value = _value;
            if (Double.IsNaN(value))
                yield break;

            yield return ToJson("", value.ToString(MetricsCollector.DOUBLE_FORMAT), unixTimestamp);
        }

        /// <summary>
        /// Records the current value of the gauge. Use Double.NaN to disable this gauge.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>The previous value.</returns>
        public double Record(double value)
        {
            return Interlocked.Exchange(ref _value, value);
        }
    }
}
