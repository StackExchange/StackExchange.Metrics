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
        private enum SnapshotReportingMode
        {
            None,
            CountOnly,
            All
        }

        private static readonly Dictionary<Type, GaugeAggregatorStrategy> _aggregatorsByTypeCache = new Dictionary<Type, GaugeAggregatorStrategy>();

        public static Func<int> GetDefaultMinimumEvents { get; set; } = () => 1;

        private readonly object _recordLock = new object();
        private readonly double[] _percentiles;
        private readonly string[] _suffixes;

        private readonly bool _trackMean;
        private readonly bool _specialCaseMax;
        private readonly bool _specialCaseMin;
        private readonly bool _specialCaseLast;
        private readonly bool _reportCount;

        private readonly double[] _snapshot;
        private SnapshotReportingMode _snapshotReportingMode;

        private List<double> _list;
        private List<double> _warmlist;
        private double _min = Double.PositiveInfinity;
        private double _max = Double.NegativeInfinity;
        private double _last;
        private double _sum = 0;
        private int _count = 0;

        public override string MetricType => "gauge";

        /// <summary>
        /// Determines the minimum number of events which need to be recorded in any given reporting interval
        /// before they will be aggregated and reported. If this threshold is not met, the recorded data points
        /// will be discarded at the end of the reporting interval.
        /// </summary>
        public virtual int MinimumEvents => GetDefaultMinimumEvents();

        public AggregateGauge()
        {
            var strategy = GetAggregatorStategy();
            // denormalize these for one less level of indirection
            _percentiles= strategy.Percentiles;
            _suffixes = strategy.Suffixes;
            _trackMean = strategy.TrackMean;
            _specialCaseMin = strategy.SpecialCaseMin;
            _specialCaseMax = strategy.SpecialCaseMax;
            _specialCaseLast = strategy.SpecialCaseLast;
            _reportCount = strategy.ReportCount;

            // setup heap, if required.
            if (strategy.UseList)
                _list = new List<double>();

            _snapshot = new double[_suffixes.Length];
            _snapshotReportingMode = SnapshotReportingMode.None;
        }

        protected override string[] GetImmutableSuffixesArray()
        {
            return _suffixes;
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

        public override string GetDescription(int suffixIndex)
        {
            if (!String.IsNullOrEmpty(Description))
            {
                switch (PercentileToAggregateMode(_percentiles[suffixIndex]))
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
                        return Description + " (" + DoubleToPercentileString(_percentiles[suffixIndex]) + ")";
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

        protected override void Serialize(MetricWriter writer, string unixTimestamp)
        {
            var mode = _snapshotReportingMode;
            if (mode == SnapshotReportingMode.None)
                return;

            var countOnly = mode == SnapshotReportingMode.CountOnly;
            for (var i = 0; i < _percentiles.Length; i++)
            {
                if (countOnly && PercentileToAggregateMode(_percentiles[i]) != AggregateMode.Count)
                    continue;

                WriteValue(writer, _snapshot[i], unixTimestamp, i);
            }
        }

        protected override void PreSerialize()
        {
            CaptureSnapshot();
        }

        private void CaptureSnapshot()
        {
            List<double> list = null;
            double last;
            double min;
            double max;
            int count;
            double sum;

            lock (_recordLock)
            {
                if (_count > 0)
                {
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
            
            if (count == 0 || count < MinimumEvents)
            {
                _snapshotReportingMode = _reportCount ? SnapshotReportingMode.CountOnly : SnapshotReportingMode.None;
            }
            else
            {
                _snapshotReportingMode = SnapshotReportingMode.All;
            }

            if (_snapshotReportingMode != SnapshotReportingMode.None)
            {
                var countOnly = _snapshotReportingMode == SnapshotReportingMode.CountOnly;

                var lastIndex = 0;

                if (!countOnly && list != null)
                {
                    lastIndex = list.Count - 1;

                    if (!_specialCaseLast)
                        last = list[lastIndex];

                    list.Sort();
                }

                for (var i = 0; i < _percentiles.Length; i++)
                {
                    var mode = PercentileToAggregateMode(_percentiles[i]);
                    if (countOnly && mode != AggregateMode.Count)
                        continue;

                    double value;
                    switch(mode)
                    {
                        case AggregateMode.Average:
                            value = sum/count;
                            break;
                        case AggregateMode.Median:
                        case AggregateMode.Percentile:
                            var index = (int)Math.Round(_percentiles[i] * lastIndex);
                            value = list[index];
                            break;
                        case AggregateMode.Max:
                            value = _specialCaseMax ? max : list[lastIndex];
                            break;
                        case AggregateMode.Min:
                            value = _specialCaseMin ? min : list[0];
                            break;
                        case AggregateMode.Last:
                            value = last;
                            break;
                        case AggregateMode.Count:
                            value = count;
                            break;
                        default:
                            throw new NotImplementedException();
                    }

                    _snapshot[i] = value;
                }
            }

            if (list != null && list.Count*2 >= list.Capacity)
            {
                // if at least half of the list capacity was used, then we'll consider re-using this list.
                list.Clear();
                lock (_recordLock)
                {
                    _warmlist = list;
                }
            }
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

        [SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
        internal static AggregateMode PercentileToAggregateMode(double percentile)
        {
            if (percentile < 0.0)
            {
                if (percentile == -1.0)
                    return AggregateMode.Average;
                if (percentile == -2.0)
                    return AggregateMode.Last;
                if (percentile == -3.0)
                    return AggregateMode.Count;

                throw new Exception($"Percentile {percentile} is invalid.");
            }

            if (percentile == 0.0)
                return AggregateMode.Min;
            if (percentile == 0.5)
                return AggregateMode.Median;
            if (percentile == 1.0)
                return AggregateMode.Max;
            if (percentile < 1.0)
                return AggregateMode.Percentile;

            throw new Exception($"Percentile {percentile} is invalid.");
        }

        internal static double AggregateModeToPercentileAndSuffix(AggregateMode mode, double percentile, out string defaultSuffix)
        {
            switch (mode)
            {
                case AggregateMode.Average:
                    defaultSuffix = "_avg";
                    return -1.0;
                case AggregateMode.Last:
                    defaultSuffix = "";
                    return -2.0;
                case AggregateMode.Count:
                    defaultSuffix = "_count";
                    return -3.0;
                case AggregateMode.Median:
                    defaultSuffix = "_median";
                    return 0.5;
                case AggregateMode.Percentile:
                    if (Double.IsNaN(percentile) || percentile < 0 || percentile > 1)
                        throw new ArgumentOutOfRangeException(nameof(percentile), "Percentile must be specified for gauge aggregators with percentile mode. Percentile must be between 0 and 1 (inclusive)");

                    defaultSuffix = "_" + (int) (percentile*100);
                    return percentile;
                case AggregateMode.Max:
                    defaultSuffix = "_max";
                    return 1.0;
                case AggregateMode.Min:
                    defaultSuffix = "_min";
                    return 0.0;
                default:
                    throw new Exception("Gauge mode not implemented.");
            }
        }

        private struct AggregateInfo
        {
            public double Percentile { get; }
            public string Suffix { get; }

            public AggregateInfo(double percentile, string suffix)
            {
                Percentile = percentile;
                Suffix = suffix;
            }
        }

        private class GaugeAggregatorStrategy
        {
            public readonly double[] Percentiles;
            public readonly string[] Suffixes;

            public readonly bool UseList;
            public readonly bool SpecialCaseMax;
            public readonly bool SpecialCaseMin;
            public readonly bool SpecialCaseLast;
            public readonly bool TrackMean;
            public readonly bool ReportCount;

            [SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
            public GaugeAggregatorStrategy(ReadOnlyCollection<GaugeAggregatorAttribute> aggregators)
            {
                Percentiles = new double[aggregators.Count];
                Suffixes = new string[aggregators.Count];

                var i = 0;
                var arbitraryPercentagesCount = 0;
                foreach (var r in aggregators)
                {
                    var percentile = r.Percentile;

                    Percentiles[i] = percentile;
                    Suffixes[i] = r.Suffix;
                    i++;

                    if (percentile < 0)
                    {
                        if (percentile == -1.0)
                        {
                            TrackMean = true;
                        }
                        else if (percentile == -2.0)
                        {
                            SpecialCaseLast = true;
                        }
                        else if (percentile == -3.0)
                        {
                            ReportCount = true;
                        }
                    }
                    else if (percentile == 0.0)
                    {
                        SpecialCaseMin = true;
                    }
                    else if (percentile == 1.0)
                    {
                        SpecialCaseMax = true;
                    }
                    else
                    {
                        arbitraryPercentagesCount++;
                    }
                }

                if (arbitraryPercentagesCount > 0)
                {
                    UseList = true;
                    SpecialCaseLast = false;
                    SpecialCaseMax = false;
                    SpecialCaseMin = false;
                }
            }
        }
    }

    public enum AggregateMode : byte
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

        public GaugeAggregatorAttribute(AggregateMode mode) : this(mode, null, Double.NaN)
        {
        }

        public GaugeAggregatorAttribute(AggregateMode mode, string suffix) : this(mode, suffix, Double.NaN)
        {
        }

        public GaugeAggregatorAttribute(AggregateMode mode, double percentile) : this(mode, null, percentile)
        {
        }

        public GaugeAggregatorAttribute(double percentile) : this(AggregateMode.Percentile, null, percentile)
        {
        }

        public GaugeAggregatorAttribute(AggregateMode aggregateMode, string suffix, double percentile)
        {
            AggregateMode = aggregateMode;

            string defaultSuffix;
            Percentile = AggregateGauge.AggregateModeToPercentileAndSuffix(aggregateMode, percentile, out defaultSuffix);
            Suffix = suffix ?? defaultSuffix;

            if (Suffix.Length > 0 && !BosunValidation.IsValidMetricName(Suffix))
                throw new Exception("\"" + Suffix + "\" is not a valid metric suffix.");
        }
    }
}
