using System;
using System.Collections.Immutable;
using System.Threading;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Record as often as you want, but only the last value recorded before the reporting interval is sent to an endpoint (it samples the current value).
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#samplinggauge
    /// </summary>
    public sealed class SamplingGauge : MetricBase
    {
        private double _value = double.NaN;

        /// <summary>
        /// Instantiates a new sampling gauge.
        /// </summary>
        internal SamplingGauge(string name, string unit, string description, MetricSourceOptions options, ImmutableDictionary<string, string> tags = null) : base(name, unit, description, options, tags)
        {
        }

        /// <summary>
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <summary>
        /// Records the current value of the gauge. Use Double.NaN to disable this gauge.
        /// </summary>
        public void Record(double value) => Interlocked.Exchange(ref _value, value);

        /// <inheritdoc/>
        protected override void WriteReadings(IMetricReadingBatch batch, DateTime timestamp)
        {
            var value = Interlocked.Exchange(ref _value, double.NaN);
            if (double.IsNaN(value))
            {
                return;
            }

            batch.Add(
                CreateReading(value, timestamp)
            );
        }
    }
}
