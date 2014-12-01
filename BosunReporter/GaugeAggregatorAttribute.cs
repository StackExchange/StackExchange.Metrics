using System;

namespace BosunReporter
{
    public enum AggregateMode
    {
        Average,
        Median,
        Percentile,
        Max,
        Min,
        Last
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class GaugeAggregatorAttribute : Attribute
    {
        public readonly AggregateMode AggregateMode;
        public readonly string Suffix;
        public readonly double Percentile;

        public GaugeAggregatorAttribute(AggregateMode mode) : this(mode, null, Double.NaN) {}
        public GaugeAggregatorAttribute(AggregateMode mode, string suffix) : this(mode, suffix, Double.NaN) {}
        public GaugeAggregatorAttribute(AggregateMode mode, double percentile) : this(mode, null, percentile) {}
        public GaugeAggregatorAttribute(double percentile) : this(AggregateMode.Percentile, null, percentile) {}

        public GaugeAggregatorAttribute(AggregateMode aggregateMode, string suffix, double percentile)
        {
            AggregateMode = aggregateMode;

            string defaultSuffix;
            switch (aggregateMode)
            {
                case AggregateMode.Average:
                    Percentile = -1.0;
                    defaultSuffix = "_avg";
                    break;
                case AggregateMode.Last:
                    Percentile = -2.0;
                    defaultSuffix = "";
                    break;
                case AggregateMode.Median:
                    Percentile = 0.5;
                    defaultSuffix = "_median";
                    break;
                case AggregateMode.Percentile:
                    if (Double.IsNaN(percentile) || percentile < 0 || percentile > 1)
                        throw new ArgumentOutOfRangeException("percentile", "Percentile must be specified for gauge aggregators with percentile mode. Percentile must be between 0 and 1 (inclusive)");
                    Percentile = percentile;
                    defaultSuffix = "_" + (int)(percentile*100);
                    break;
                case AggregateMode.Max:
                    Percentile = 1.0;
                    defaultSuffix = "_max";
                    break;
                case AggregateMode.Min:
                    Percentile = 0.0;
                    defaultSuffix = "_min";
                    break;
                default:
                    throw new Exception("Gauge mode not implemented.");
            }

            Suffix = suffix ?? defaultSuffix;
            if (Suffix.Length > 0 && !Validation.IsValidMetricName(Suffix))
                throw new Exception("\"" + Suffix + "\" is not a valid metric suffix.");
        }
    }
}
