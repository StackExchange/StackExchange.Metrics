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
    /// <summary>
    /// Aggregates data points (min, max, avg, median, etc) before sending them to Bosun. Good for recording high-volume events. You must inherit from this
    /// class in order to use it. See https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#aggregategauge
    /// </summary>
    public abstract class AggregateGauge : BosunMetric
    {
        enum SnapshotReportingMode
        {
            None,
            CountOnly,
            All
        }

        static readonly Dictionary<Type, GaugeAggregatorStrategy> _aggregatorsByTypeCache = new Dictionary<Type, GaugeAggregatorStrategy>();

        /// <summary>
        /// A delegate which backs the <see cref="MinimumEvents"/> property.
        /// </summary>
        public static Func<int> GetDefaultMinimumEvents { get; set; } = () => 1;

        readonly object _recordLock = new object();
        readonly double[] _percentiles;
        readonly string[] _suffixes;

        readonly bool _trackMean;
        readonly bool _specialCaseMax;
        readonly bool _specialCaseMin;
        readonly bool _specialCaseLast;
        readonly bool _reportCount;

        readonly double[] _snapshot;
        SnapshotReportingMode _snapshotReportingMode;

        List<double> _list;
        List<double> _warmlist;
        double _min = double.PositiveInfinity;
        double _max = double.NegativeInfinity;
        double _last;
        double _sum = 0;
        int _count = 0;

        /// <summary>
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override string MetricType => "gauge";

        /// <summary>
        /// Determines the minimum number of events which need to be recorded in any given reporting interval
        /// before they will be aggregated and reported. If this threshold is not met, the recorded data points
        /// will be discarded at the end of the reporting interval.
        /// </summary>
        public virtual int MinimumEvents => GetDefaultMinimumEvents();

        /// <summary>
        /// Protected constructor for calling from child classes.
        /// </summary>
        protected AggregateGauge()
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

        /// <summary>
        /// See <see cref="BosunMetric.GetImmutableSuffixesArray"/>
        /// </summary>
        protected override string[] GetImmutableSuffixesArray()
        {
            return _suffixes;
        }

        /// <summary>
        /// Records a data point on the aggregate gauge. This will likely not be sent to Bosun as an individual datapoint. Instead, it will be aggregated with
        /// other data points and sent as one or more aggregates (controlled by which AggregateModes were applied to the metric).
        /// </summary>
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

        /// <summary>
        /// See <see cref="BosunMetric.GetDescription"/>
        /// </summary>
        public override string GetDescription(int suffixIndex)
        {
            if (!string.IsNullOrEmpty(Description))
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

        static string DoubleToPercentileString(double pct)
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

        /// <summary>
        /// See <see cref="BosunMetric.Serialize"/>
        /// </summary>
        protected override void Serialize(MetricWriter writer, DateTime now)
        {
            var mode = _snapshotReportingMode;
            if (mode == SnapshotReportingMode.None)
                return;

            var countOnly = mode == SnapshotReportingMode.CountOnly;
            for (var i = 0; i < _percentiles.Length; i++)
            {
                if (countOnly && PercentileToAggregateMode(_percentiles[i]) != AggregateMode.Count)
                    continue;

                WriteValue(writer, _snapshot[i], now, i);
            }
        }

        /// <summary>
        /// See <see cref="BosunMetric.PreSerialize"/>
        /// </summary>
        protected override void PreSerialize()
        {
            CaptureSnapshot();
        }

        void CaptureSnapshot()
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
                _min = double.PositiveInfinity;
                max = _max;
                _max = double.NegativeInfinity;
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

        GaugeAggregatorStrategy GetAggregatorStategy()
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
            if (mode != AggregateMode.Percentile && !double.IsNaN(percentile))
                throw new ArgumentOutOfRangeException(nameof(percentile), "Percentile cannot be specified for mode " + mode);

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
                    if (double.IsNaN(percentile) || percentile < 0 || percentile > 1)
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

        struct AggregateInfo
        {
            public double Percentile { get; }
            public string Suffix { get; }

            public AggregateInfo(double percentile, string suffix)
            {
                Percentile = percentile;
                Suffix = suffix;
            }
        }

        class GaugeAggregatorStrategy
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

    /// <summary>
    /// Enumeration of aggregation modes supported by <see cref="AggregateGauge"/>.
    /// </summary>
    public enum AggregateMode : byte
    {
        /// <summary>
        /// The arithmetic mean of all values recorded during a given interval. Uses the suffix "_avg" by default.
        /// </summary>
        Average,
        /// <summary>
        /// The median value recorded during a given interval. Uses the suffix "_median" by default.
        /// </summary>
        Median,
        /// <summary>
        /// An arbitrary percentile of values recorded during a given interval. Uses the suffix "_XX" by default, where XX is the first two decimal places of
        /// the percentile. For example, the 95% percentile would use the suffix "_95".
        /// </summary>
        Percentile,
        /// <summary>
        /// The maximum value recorded during a given interval. Uses the suffix "_max" by default.
        /// </summary>
        Max,
        /// <summary>
        /// The minimum value recorded during a given interval. Uses the suffix "_min" by default.
        /// </summary>
        Min,
        /// <summary>
        /// The last (final) value recorded during a given interval. Uses the suffix "" (empty string) by default.
        /// </summary>
        Last,
        /// <summary>
        /// The number of values recorded during a given interval. Uses the suffix "_count" by default.
        /// </summary>
        Count,
    }

    /// <summary>
    /// Applies an <see cref="AggregateMode"/> to an <see cref="AggregateGauge"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class GaugeAggregatorAttribute : Attribute
    {
        /// <summary>
        /// The aggregator mode.
        /// </summary>
        public readonly AggregateMode AggregateMode;
        /// <summary>
        /// The metric name suffix for this aggregator.
        /// </summary>
        public readonly string Suffix;
        /// <summary>
        /// The percentile of this aggregator, if applicable. Otherwise, NaN.
        /// </summary>
        public readonly double Percentile;

        /// <summary>
        /// Applies an <see cref="AggregateMode"/> to an <see cref="AggregateGauge"/>.
        /// </summary>
        /// <param name="mode">The aggregate mode. Don't use AggregateMode.Percentile with this constructor.</param>
        public GaugeAggregatorAttribute(AggregateMode mode) : this(mode, null, double.NaN)
        {
        }

        /// <summary>
        /// Applies an <see cref="AggregateMode"/> to an <see cref="AggregateGauge"/>.
        /// </summary>
        /// <param name="mode">The aggregate mode. Don't use AggregateMode.Percentile with this constructor.</param>
        /// <param name="suffix">Overrides the default suffix for the aggregate mode.</param>
        public GaugeAggregatorAttribute(AggregateMode mode, string suffix) : this(mode, suffix, double.NaN)
        {
        }

        /// <summary>
        /// Applies an <see cref="AggregateMode"/> to an <see cref="AggregateGauge"/>.
        /// </summary>
        /// <param name="mode">The aggregate mode. Should always be AggregateMode.Percentile for this constructor.</param>
        /// <param name="percentile">
        /// The percentile represented as a double. For example, 0.95 = 95th percentile. Using more than two digits is not recommended.
        /// </param>
        public GaugeAggregatorAttribute(AggregateMode mode, double percentile) : this(mode, null, percentile)
        {
        }

        /// <summary>
        /// Applies a percentile aggregator to an <see cref="AggregateGauge"/>.
        /// </summary>
        /// <param name="percentile">
        /// The percentile represented as a double. For example, 0.95 = 95th percentile. Using more than two digits is not recommended.
        /// </param>
        public GaugeAggregatorAttribute(double percentile) : this(AggregateMode.Percentile, null, percentile)
        {
        }

        /// <summary>
        /// Applies an <see cref="AggregateMode"/> to an <see cref="AggregateGauge"/>.
        /// </summary>
        /// <param name="mode">The aggregate mode. Don't use AggregateMode.Percentile with this constructor.</param>
        /// <param name="suffix">Overrides the default suffix for the aggregate mode.</param>
        /// <param name="percentile">
        /// The percentile represented as a double. For example, 0.95 = 95th percentile. Using more than two digits is not recommended. In order to use this
        /// argument, <paramref name="mode"/> must be AggregateMode.Percentile.
        /// </param>
        public GaugeAggregatorAttribute(AggregateMode mode, string suffix, double percentile)
        {
            AggregateMode = mode;

            string defaultSuffix;
            Percentile = AggregateGauge.AggregateModeToPercentileAndSuffix(mode, percentile, out defaultSuffix);
            Suffix = suffix ?? defaultSuffix;

            if (Suffix.Length > 0 && !BosunValidation.IsValidMetricName(Suffix))
                throw new Exception("\"" + Suffix + "\" is not a valid metric suffix.");
        }
    }
}
