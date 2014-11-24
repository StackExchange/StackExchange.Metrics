using System;

namespace BosunReporter
{
    public enum AggregateMode
    {
        Average,
        Median,
        Percentile,
        Max,
        Min
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class GaugeAggregatorAttribute : Attribute
    {
        public readonly AggregateMode AggregateMode;
        public readonly string Name;
        public readonly double Percentile;

        public GaugeAggregatorAttribute(AggregateMode mode) : this(mode, null, Double.NaN) {}
        public GaugeAggregatorAttribute(AggregateMode mode, string name) : this(mode, name, Double.NaN) {}
        public GaugeAggregatorAttribute(AggregateMode mode, double percentile) : this(mode, null, percentile) {}
        public GaugeAggregatorAttribute(double percentile) : this(AggregateMode.Percentile, null, percentile) {}

        public GaugeAggregatorAttribute(AggregateMode aggregateMode, string name, double percentile)
        {
            AggregateMode = aggregateMode;

            string defaultName = null;
            switch (aggregateMode)
            {
                case AggregateMode.Average:
                    Percentile = -1.0;
                    defaultName = "average";
                    break;
                case AggregateMode.Median:
                    Percentile = 0.5;
                    defaultName = "median";
                    break;
                case AggregateMode.Percentile:
                    if (Double.IsNaN(percentile) || percentile < 0 || percentile > 1)
                        throw new ArgumentOutOfRangeException("percentile", "Percentile must be specified for gauge aggregators with percentile mode. Percentile must be between 0 and 1 (inclusive)");
                    Percentile = percentile;
                    defaultName = ((int) (percentile*100)).ToString();
                    break;
                case AggregateMode.Max:
                    Percentile = 1.0;
                    defaultName = "max";
                    break;
                case AggregateMode.Min:
                    Percentile = 0.0;
                    defaultName = "min";
                    break;
                default:
                    throw new Exception("Gauge mode not implemented.");
            }

            Name = String.IsNullOrEmpty(name) ? defaultName : name;
            if (!Validation.IsValidTagValue(Name))
                throw new Exception("\"" + Name + "\" is not a valid tag value.");
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class GaugeAggregatorTagNameAttribute : Attribute
    {
        public readonly string Name;

        public GaugeAggregatorTagNameAttribute(string name)
        {
            Name = name;
            if (!Validation.IsValidTagName(Name))
                throw new Exception("\"" + Name + "\" is not a valid tag name.");
        }
    }
}
