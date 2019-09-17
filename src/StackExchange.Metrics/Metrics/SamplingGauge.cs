using StackExchange.Metrics.Infrastructure;
using System;
using System.Threading;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Record as often as you want, but only the last value recorded before the reporting interval is sent to Bosun (it samples the current value).
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#samplinggauge
    /// </summary>
    public class SamplingGauge : MetricBase
    {
        double _value = double.NaN;

        /// <summary>
        /// The current value of the gauge.
        /// </summary>
        public double CurrentValue => _value;

        /// <summary>
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <summary>
        /// See <see cref="MetricBase.Serialize"/>
        /// </summary>
        protected override void Serialize(IMetricBatch writer, DateTime now)
        {
            var value = _value;
            if (double.IsNaN(value))
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
