using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Aggregates data points (min, max, avg, median, etc) before sending them to a handler. Good for recording high-volume events.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#aggregategauge
    /// </summary>
    public sealed class AggregateGauge : MetricBase
    {
        enum SnapshotReportingMode
        {
            None,
            CountOnly,
            All,
        }

        /// <summary>
        /// A delegate which backs the <see cref="MinimumEvents"/> property.
        /// </summary>
        public static Func<int> GetDefaultMinimumEvents { get; set; } = () => 1;

        readonly object _recordLock = new object();
        readonly ImmutableArray<double> _percentiles;
        readonly ImmutableArray<string> _suffixes;

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
        /// Instantiates a new <see cref="AggregateGauge"/>.
        /// </summary>
        public AggregateGauge(IEnumerable<GaugeAggregator> aggregators, string name, string unit, string description, MetricSourceOptions options, ImmutableDictionary<string, string> tags = null) : base(name, unit, description, options, tags)
        {
            var strategy = new GaugeAggregatorStrategy(aggregators.ToImmutableArray());
            // denormalize these for one less level of indirection
            _percentiles = strategy.Percentiles;
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
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <summary>
        /// Determines the minimum number of events which need to be recorded in any given reporting interval
        /// before they will be aggregated and reported. If this threshold is not met, the recorded data points
        /// will be discarded at the end of the reporting interval.
        /// </summary>
        public int MinimumEvents => GetDefaultMinimumEvents();

        /// <summary>
        /// Records a data point on the aggregate gauge. This will likely not be sent to Bosun as an individual datapoint. Instead, it will be aggregated with
        /// other data points and sent as one or more aggregates (controlled by which AggregateModes were applied to the metric).
        /// </summary>
        public void Record(double value)
        {
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

        /// <inheritdoc/>
        protected override IEnumerable<SuffixMetadata> GetSuffixMetadata()
        {
            static string GetDescription(string baseDescription, double percentile) =>
                (PercentileToAggregateMode(percentile)) switch
                {
                    AggregateMode.Last => baseDescription + " (last)",
                    AggregateMode.Average => baseDescription + " (average)",
                    AggregateMode.Max => baseDescription + " (maximum)",
                    AggregateMode.Min => baseDescription + " (minimum)",
                    AggregateMode.Median => baseDescription + " (median)",
                    AggregateMode.Percentile => baseDescription + " (" + DoubleToPercentileString(percentile) + ")",
                    AggregateMode.Count => baseDescription + " (count of the number of events recorded)",
                    _ => baseDescription,
                };

            for (var i = 0; i < _percentiles.Length; i++)
            {
                var name = Name + _suffixes[i];
                yield return new SuffixMetadata(name, Unit, GetDescription(Description, _percentiles[i]));
            }
        }

        static string DoubleToPercentileString(double pct)
        {
            var ip = (int)(pct * 100.0);
            var lastDigit = ip % 10;
            string ending = lastDigit switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };
            return ip + ending + " percentile";
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
                            Debug.WriteLine("StackExchange.Metrics: Re-using pre-warmed list for aggregate gauge.");
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

        /// <inheritdoc/>
        protected override void WriteReadings(IMetricReadingBatch batch, DateTime timestamp)
        {
            CaptureSnapshot();

            var mode = _snapshotReportingMode;
            if (mode == SnapshotReportingMode.None)
            {
                return;
            }

            var countOnly = mode == SnapshotReportingMode.CountOnly;
            var suffixes = Suffixes;
            for (var i = 0; i < _percentiles.Length; i++)
            {
                if (countOnly && PercentileToAggregateMode(_percentiles[i]) != AggregateMode.Count)
                {
                    continue;
                }

                batch.Add(
                    CreateReading(suffixes[i], _snapshot[i], timestamp)
                );
            }
        }

        class GaugeAggregatorStrategy
        {
            public ImmutableArray<double> Percentiles { get; }
            public ImmutableArray<string> Suffixes { get; }
            public bool UseList { get; }
            public bool SpecialCaseMax { get; }
            public bool SpecialCaseMin { get; }
            public bool SpecialCaseLast { get; }
            public bool TrackMean { get; }
            public bool ReportCount { get; }

            [SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
            public GaugeAggregatorStrategy(ImmutableArray<GaugeAggregator> aggregators)
            {
                var percentiles = ImmutableArray.CreateBuilder<double>(aggregators.Length);
                var suffixes = ImmutableArray.CreateBuilder<string>(aggregators.Length);

                var arbitraryPercentagesCount = 0;
                var knownSuffixes = new HashSet<string>();
                foreach (var r in aggregators)
                {
                    if (knownSuffixes.Contains(r.Suffix))
                    {
                        throw new ArgumentException($"More than one gauge aggregator with the name \"{r.Suffix}\".", nameof(aggregators));
                    }

                    knownSuffixes.Add(r.Suffix);

                    var percentile = r.Percentile;

                    percentiles.Add(percentile);
                    suffixes.Add(r.Suffix);

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

                Percentiles = percentiles.MoveToImmutable();
                Suffixes = suffixes.MoveToImmutable();
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
    /// Represents the metadata for different kinds of <see cref="AggregateGauge"/> measurements.
    /// </summary>
    public readonly struct GaugeAggregator
    {
        /// <summary>
        /// Gets a <see cref="GaugeAggregator"/> that represents the mean value recorded in a given interval.
        /// </summary>
        public static readonly GaugeAggregator Average = new GaugeAggregator(AggregateMode.Average);
        /// <summary>
        /// Gets a <see cref="GaugeAggregator"/> that represents the median value recorded in a given interval.
        /// </summary>
        public static readonly GaugeAggregator Median = new GaugeAggregator(AggregateMode.Median);
        /// <summary>
        /// Gets a <see cref="GaugeAggregator"/> that represents the 95% percentile recorded in a given interval.
        /// </summary>
        public static readonly GaugeAggregator Percentile_95 = new GaugeAggregator(AggregateMode.Percentile, 0.95);
        /// <summary>
        /// Gets a <see cref="GaugeAggregator"/> that represents the 99% percentile recorded in a given interval.
        /// </summary>
        public static readonly GaugeAggregator Percentile_99 = new GaugeAggregator(AggregateMode.Percentile, 0.99);
        /// <summary>
        /// Gets a <see cref="GaugeAggregator"/> that represents the maximum value recorded in a given interval.
        /// </summary>
        public static readonly GaugeAggregator Max = new GaugeAggregator(AggregateMode.Max);
        /// <summary>
        /// Gets a <see cref="GaugeAggregator"/> that represents the minimum value recorded in a given interval.
        /// </summary>
        public static readonly GaugeAggregator Min = new GaugeAggregator(AggregateMode.Min);
        /// <summary>
        /// Gets a <see cref="GaugeAggregator"/> that represents the count of values recorded in a given interval.
        /// </summary>
        public static readonly GaugeAggregator Count = new GaugeAggregator(AggregateMode.Count);
        /// <summary>
        /// Gets an <see cref="IEnumerable{T}"/> of <see cref="GaugeAggregator"/> that are typically used.
        /// </summary>
        public static readonly IEnumerable<GaugeAggregator> Default = ImmutableArray.Create(
            Average, Median, Percentile_95, Percentile_99, Max, Min, Count
        );

        /// <summary>
        /// The aggregator mode.
        /// </summary>
        public AggregateMode AggregateMode { get; }
        /// <summary>
        /// The metric name suffix for this aggregator.
        /// </summary>
        public string Suffix { get; }
        /// <summary>
        /// The percentile of this aggregator, if applicable. Otherwise, NaN.
        /// </summary>
        public double Percentile { get; }

        /// <summary>
        /// Applies an <see cref="AggregateMode"/> to an <see cref="AggregateGauge"/>.
        /// </summary>
        /// <param name="mode">The aggregate mode. Don't use AggregateMode.Percentile with this constructor.</param>
        public GaugeAggregator(AggregateMode mode) : this(mode, null, double.NaN)
        {
        }

        /// <summary>
        /// Applies an <see cref="AggregateMode"/> to an <see cref="AggregateGauge"/>.
        /// </summary>
        /// <param name="mode">The aggregate mode. Don't use AggregateMode.Percentile with this constructor.</param>
        /// <param name="suffix">Overrides the default suffix for the aggregate mode.</param>
        public GaugeAggregator(AggregateMode mode, string suffix) : this(mode, suffix, double.NaN)
        {
        }

        /// <summary>
        /// Applies an <see cref="AggregateMode"/> to an <see cref="AggregateGauge"/>.
        /// </summary>
        /// <param name="mode">The aggregate mode. Should always be AggregateMode.Percentile for this constructor.</param>
        /// <param name="percentile">
        /// The percentile represented as a double. For example, 0.95 = 95th percentile. Using more than two digits is not recommended.
        /// </param>
        public GaugeAggregator(AggregateMode mode, double percentile) : this(mode, null, percentile)
        {
        }

        /// <summary>
        /// Applies a percentile aggregator to an <see cref="AggregateGauge"/>.
        /// </summary>
        /// <param name="percentile">
        /// The percentile represented as a double. For example, 0.95 = 95th percentile. Using more than two digits is not recommended.
        /// </param>
        public GaugeAggregator(double percentile) : this(AggregateMode.Percentile, null, percentile)
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
        public GaugeAggregator(AggregateMode mode, string suffix, double percentile)
        {
            AggregateMode = mode;
            Percentile = AggregateGauge.AggregateModeToPercentileAndSuffix(mode, percentile, out var defaultSuffix);
            Suffix = suffix ?? defaultSuffix;

            if (Suffix.Length > 0 && !MetricValidation.IsValidMetricName(Suffix))
                throw new Exception("\"" + Suffix + "\" is not a valid metric suffix.");
        }
    }
}
