using System;
using System.Threading;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    /// <summary>
    /// Record as often as you want, but only the last value recorded before the reporting interval is sent to Bosun (it samples the current value).
    /// See https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#samplinggauge
    /// </summary>
    public class SamplingGauge : BosunMetric
    {
        private double _value = Double.NaN;

        /// <summary>
        /// The current value of the gauge.
        /// </summary>
        public double CurrentValue => _value;

        /// <summary>
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override string MetricType => "gauge";

        /// <summary>
        /// See <see cref="BosunMetric.Serialize"/>
        /// </summary>
        protected override void Serialize(MetricWriter writer, DateTime now)
        {
            var value = _value;
            if (Double.IsNaN(value))
                return;

            WriteValue(writer, value, now);
        }

        /// <summary>
        /// Records the current value of the gauge. Use Double.NaN to disable this gauge.
        /// </summary>
        public void Record(double value)
        {
            AssertAttached();
            Interlocked.Exchange(ref _value, value);
        }
    }
}
