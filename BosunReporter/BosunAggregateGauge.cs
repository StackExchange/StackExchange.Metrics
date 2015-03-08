using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace BosunReporter
{
    [GaugeAggregator(AggregateMode.Last)]
    public class BosunAggregateGauge : BosunMetric, IDoubleGauge
    {
        private static readonly Dictionary<Type, GaugeAggregatorStrategy> _aggregatorsByTypeCache = new Dictionary<Type, GaugeAggregatorStrategy>();

        private readonly object _recordLock = new object();
        private readonly GaugeAggregatorStrategy _aggregatorStrategy;

        private readonly bool _trackMean;
        private readonly bool _specialCaseMax;
        private readonly bool _specialCaseMin;
        private readonly bool _specialCaseLast;

        private List<double> _list;
        private double _min = Double.PositiveInfinity;
        private double _max = Double.NegativeInfinity;
        private double _last;
        private double _sum = 0;
        private int _count = 0;

        public override string MetricType => "gauge";

        public override IReadOnlyCollection<string> Suffixes => _aggregatorStrategy.Suffixes;

        public BosunAggregateGauge()
        {
            _aggregatorStrategy = GetAggregatorStategy();
            // denormalize these for one less level of indirection
            _trackMean = _aggregatorStrategy.TrackMean;
            _specialCaseMin = _aggregatorStrategy.SpecialCaseMin;
            _specialCaseMax = _aggregatorStrategy.SpecialCaseMax;
            _specialCaseLast = _aggregatorStrategy.SpecialCaseLast;

            // setup heap, if required.
            if (_aggregatorStrategy.UseList)
                _list = new List<double>();
        }

        public void Record(double value)
        {
            if (!IsAttached)
            {
                var ex = new InvalidOperationException("Attempting to record on a gauge which is not attached to a BosunReporter object.");
                try
                {
                    ex.Data["Metric"] = Name;
                    ex.Data["Tags"] = SerializedTags;
                }
                finally
                {
                    throw ex;
                }
            }

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

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            var snapshot = GetSnapshot();
            if (snapshot == null)
                yield break;

            foreach (var a in _aggregatorStrategy.Aggregators)
            {
                yield return ToJson(a.Suffix, snapshot[a.Percentile].ToString(MetricsCollector.DOUBLE_FORMAT), unixTimestamp);
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
                    _list = new List<double>();

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
                        throw new Exception(String.Format("{0} has more than one gauge aggregator with the name \"{1}\".", type.FullName, r.Suffix));
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
            public readonly ReadOnlyCollection<double> Percentiles;

            public GaugeAggregatorStrategy(ReadOnlyCollection<GaugeAggregatorAttribute> aggregators)
            {
                Aggregators = aggregators;

                var percentiles = new List<double>();
                var suffixes = new List<string>();

                foreach (var r in aggregators)
                {
                    suffixes.Add(r.Suffix);

                    if (r.Percentile == -1.0)
                    {
                        TrackMean = true;
                    }
                    else if (r.Percentile == -2.0)
                    {
                        SpecialCaseLast = true;
                        TrackLast = true;
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
}
