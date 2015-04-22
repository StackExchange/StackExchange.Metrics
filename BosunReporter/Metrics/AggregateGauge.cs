using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using BosunReporter.Infrastructure;

namespace BosunReporter.Metrics
{
    [GaugeAggregator(AggregateMode.Last)]
    public class AggregateGauge : BosunMetric, IDoubleGauge
    {
        private static readonly Dictionary<Type, GaugeAggregatorStrategy> _aggregatorsByTypeCache = new Dictionary<Type, GaugeAggregatorStrategy>();

        private readonly object _recordLock = new object();
        private readonly GaugeAggregatorStrategy _aggregatorStrategy;

        private readonly bool _trackMean;
        private readonly bool _specialCaseMax;
        private readonly bool _specialCaseMin;
        private readonly bool _specialCaseLast;
        private readonly bool _reportCount;

        private List<double> _list;
        private List<double> _warmlist;
        private double _min = Double.PositiveInfinity;
        private double _max = Double.NegativeInfinity;
        private double _last;
        private double _sum = 0;
        private int _count = 0;

        public override string MetricType => "gauge";

        public AggregateGauge()
        {
            _aggregatorStrategy = GetAggregatorStategy();
            // denormalize these for one less level of indirection
            _trackMean = _aggregatorStrategy.TrackMean;
            _specialCaseMin = _aggregatorStrategy.SpecialCaseMin;
            _specialCaseMax = _aggregatorStrategy.SpecialCaseMax;
            _specialCaseLast = _aggregatorStrategy.SpecialCaseLast;
            _reportCount = _aggregatorStrategy.ReportCount;

            // setup heap, if required.
            if (_aggregatorStrategy.UseList)
                _list = new List<double>();
        }

        protected override IEnumerable<string> GetSuffixes()
        {
            return _aggregatorStrategy.Suffixes;
        }

        public void Record(double value)
        {
            AssertAttached();

            lock (_recordLock)
            {
                _count++;
                if (_trackMean)
                {
                    _sum += value;
                }
                if (_specialCaseLast)
                {
                    _last = value;
                }
                if (_specialCaseMax)
                {
                    if (value > _max)
                        _max = value;
                }
                if (_specialCaseMin)
                {
                    if (value < _min)
                        _min = value;
                }
                if (_list != null)
                {
                    _list.Add(value);
                }
            }
        }

        public override string GetDescription(string suffix)
        {
            if (!String.IsNullOrEmpty(Description))
            {
                var aggregator = _aggregatorStrategy.Aggregators.First(a => a.Suffix == suffix);

                switch (aggregator.AggregateMode)
                {
                    case AggregateMode.Last:
                        return Description + " (last)";
                    case AggregateMode.Average:
                        return Description + " (average)";
                    case AggregateMode.Max:
                        return Description + " (maximum)";
                    case AggregateMode.Min:
                        return Description + " (minimum)";
                    case AggregateMode.Median:
                        return Description + " (median)";
                    case AggregateMode.Percentile:
                        return Description + " (" + DoubleToPercentileString(aggregator.Percentile) + ")";
                    case AggregateMode.Count:
                        return Description + " (count of the number of events recorded)";
                }
            }

            return Description;
        }

        private static string DoubleToPercentileString(double pct)
        {
            var ip = (int)(pct * 100.0);
            var lastDigit = ip % 10;

            string ending;
            switch (lastDigit)
            {
                case 1:
                    ending = "st";
                    break;
                case 2:
                    ending = "nd";
                    break;
                case 3:
                    ending = "rd";
                    break;
                default:
                    ending = "th";
                    break;
            }

            return ip + ending + " percentile";
        }

        protected override IEnumerable<string> Serialize(string unixTimestamp)
        {
            var snapshot = GetSnapshot();
            if (snapshot == null)
                yield break;

            foreach (var a in _aggregatorStrategy.Aggregators)
            {
                yield return ToJson(a.Suffix, snapshot[a.Percentile], unixTimestamp);
            }
        }

        private Dictionary<double, double> GetSnapshot()
        {
            List<double> list;
            double last;
            double min;
            double max;
            int count;
            double sum;

            lock (_recordLock)
            {
                if (_count == 0) // there's no data to report if count == 0
                    return null;

                list = _list;
                if (list != null)
                {
#if DEBUG
                    if (_warmlist != null)
                        Debug.WriteLine("BosunReporter: Re-using pre-warmed list for aggregate gauge.");
#endif
                    _list = _warmlist ?? new List<double>();
                    _warmlist = null;
                }

                last = _last;
                min = _min;
                _min = Double.PositiveInfinity;
                max = _max;
                _max = Double.NegativeInfinity;
                sum = _sum;
                _sum = 0;
                count = _count;
                _count = 0;
            }

            var snapshot = new Dictionary<double, double>();

            if (_reportCount)
                snapshot[-3.0] = count;
            if (_specialCaseLast)
                snapshot[-2.0] = last;
            if (_trackMean)
                snapshot[-1.0] = sum/count;
            if (_specialCaseMax)
                snapshot[1.0] = max;
            if (_specialCaseMin)
                snapshot[0.0] = min;

            if (list != null)
            {
                var lastIndex = list.Count - 1;

                if (_aggregatorStrategy.TrackLast)
                    snapshot[-2.0] = list[lastIndex];

                list.Sort();
                var percentiles = _aggregatorStrategy.Percentiles;

                foreach (var p in percentiles)
                {
                    var index = (int) Math.Round(p*lastIndex);
                    snapshot[p] = list[index];
                }

                if (list.Count * 2 >= list.Capacity)
                {
                    // if at least half of the list capacity was used, then we'll consider re-using this list.
                    list.Clear();
                    lock (_recordLock)
                    {
                        _warmlist = list;
                    }
                }
            }

            return snapshot;
        }

        private GaugeAggregatorStrategy GetAggregatorStategy()
        {
            var type = GetType();
            if (_aggregatorsByTypeCache.ContainsKey(type))
                return _aggregatorsByTypeCache[type];

            lock (_aggregatorsByTypeCache)
            {
                if (_aggregatorsByTypeCache.ContainsKey(type))
                    return _aggregatorsByTypeCache[type];

                var aggregators = GetType().GetCustomAttributes<GaugeAggregatorAttribute>(false).ToList().AsReadOnly();
                if (aggregators.Count == 0)
                    throw new Exception(GetType().FullName + " has no GaugeAggregator attributes. All gauges must have at least one.");

                var hash = new HashSet<string>();
                foreach (var r in aggregators)
                {
                    if (hash.Contains(r.Suffix))
                        throw new Exception($"{type.FullName} has more than one gauge aggregator with the name \"{r.Suffix}\".");
                }

                return _aggregatorsByTypeCache[type] = new GaugeAggregatorStrategy(aggregators);
            }
        }

        private class GaugeAggregatorStrategy
        {
            public readonly ReadOnlyCollection<GaugeAggregatorAttribute> Aggregators;
            public readonly ReadOnlyCollection<string> Suffixes;

            public readonly bool UseList;
            public readonly bool SpecialCaseMax;
            public readonly bool SpecialCaseMin;
            public readonly bool SpecialCaseLast;
            public readonly bool TrackLast;
            public readonly bool TrackMean;
            public readonly bool ReportCount;
            public readonly ReadOnlyCollection<double> Percentiles;

            [SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
            public GaugeAggregatorStrategy(ReadOnlyCollection<GaugeAggregatorAttribute> aggregators)
            {
                Aggregators = aggregators;

                var percentiles = new List<double>();
                var suffixes = new List<string>();

                foreach (var r in aggregators)
                {
                    suffixes.Add(r.Suffix);

                    if (r.Percentile < 0)
                    {
                        if (r.Percentile == -1.0)
                        {
                            TrackMean = true;
                        }
                        else if (r.Percentile == -2.0)
                        {
                            SpecialCaseLast = true;
                            TrackLast = true;
                        }
                        else if (r.Percentile == -3.0)
                        {
                            ReportCount = true;
                        }
                    }
                    else if (r.Percentile == 0.0)
                    {
                        SpecialCaseMin = true;
                    }
                    else if (r.Percentile == 1.0)
                    {
                        SpecialCaseMax = true;
                    }
                    else
                    {
                        percentiles.Add(r.Percentile);
                    }
                }

                Suffixes = suffixes.AsReadOnly();

                if (percentiles.Count > 0)
                {
                    UseList = true;
                    SpecialCaseLast = false;

                    if (SpecialCaseMax)
                    {
                        percentiles.Add(1.0);
                        SpecialCaseMax = false;
                    }

                    if (SpecialCaseMin)
                    {
                        percentiles.Add(0.0);
                        SpecialCaseMin = false;
                    }

                    percentiles.Sort();
                }

                Percentiles = percentiles.AsReadOnly();
            }
        }
    }

    public enum AggregateMode
    {
        Average,
        Median,
        Percentile,
        Max,
        Min,
        Last,
        Count,
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class GaugeAggregatorAttribute : Attribute
    {
        public readonly AggregateMode AggregateMode;
        public readonly string Suffix;
        public readonly double Percentile;

        public GaugeAggregatorAttribute(AggregateMode mode) : this(mode, null, Double.NaN) { }
        public GaugeAggregatorAttribute(AggregateMode mode, string suffix) : this(mode, suffix, Double.NaN) { }
        public GaugeAggregatorAttribute(AggregateMode mode, double percentile) : this(mode, null, percentile) { }
        public GaugeAggregatorAttribute(double percentile) : this(AggregateMode.Percentile, null, percentile) { }

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
                case AggregateMode.Count:
                    Percentile = -3.0;
                    defaultSuffix = "_count";
                    break;
                case AggregateMode.Median:
                    Percentile = 0.5;
                    defaultSuffix = "_median";
                    break;
                case AggregateMode.Percentile:
                    if (Double.IsNaN(percentile) || percentile < 0 || percentile > 1)
                        throw new ArgumentOutOfRangeException("percentile", "Percentile must be specified for gauge aggregators with percentile mode. Percentile must be between 0 and 1 (inclusive)");
                    Percentile = percentile;
                    defaultSuffix = "_" + (int)(percentile * 100);
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
            if (Suffix.Length > 0 && !BosunValidation.IsValidMetricName(Suffix))
                throw new Exception("\"" + Suffix + "\" is not a valid metric suffix.");
        }
    }
}
