using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace BosunReporter
{
    public abstract class BosunAggregateGauge : BosunMetric
    {
        private static readonly Dictionary<Type, GaugeAggregatorStrategy> _aggregatorsByTypeCache = new Dictionary<Type, GaugeAggregatorStrategy>();

        private readonly object _recordLock = new object();
        private readonly GaugeAggregatorStrategy _aggregatorStrategy;
        private readonly object _tagsLock = new object();
        private Dictionary<string, string> _tagsByAggregator;

        private readonly bool _trackMean;
        private readonly bool _trackMax;
        private readonly bool _trackMin;

        private Heap<double> _heap;
        private double _min = Double.PositiveInfinity;
        private double _max = Double.NegativeInfinity;
        private double _sum = 0;
        private int _count = 0;

        protected BosunAggregateGauge()
        {
            _aggregatorStrategy = GetAggregatorStategy();
            // denormalize these for one less level of indirection
            _trackMean = _aggregatorStrategy.TrackMean;
            _trackMin = _aggregatorStrategy.SpecialCaseMin;
            _trackMax = _aggregatorStrategy.SpecialCaseMax;

            // setup heap, if required.
            if (_aggregatorStrategy.UseMaxHeap)
                _heap = new Heap<double>(HeapMode.Max);
            else if (_aggregatorStrategy.UseMinHeap)
                _heap = new Heap<double>(HeapMode.Min);
        }

        public void Record(double value)
        {
            lock (_recordLock)
            {
                _count++;
                if (_trackMean)
                {
                    _sum += value;
                }
                if (_trackMax)
                {
                    if (value > _max)
                        _max = value;
                }
                if (_trackMin)
                {
                    if (value < _min)
                        _min = value;
                }
                if (_heap != null)
                {
                    _heap.Push(value);
                }
            }
        }

        protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
        {
            var snapshot = GetSnapshot();
            if (snapshot == null)
                yield break;

            if (_tagsByAggregator == null)
            {
                lock (_tagsLock)
                {
                    if (_tagsByAggregator == null)
                    {
                        _tagsByAggregator = new Dictionary<string, string>();
                        foreach (var a in _aggregatorStrategy.Aggregators)
                        {
                            _tagsByAggregator[a.Name] = SerializeTags(a.Name);
                        }
                    }
                }
            }

            foreach (var a in _aggregatorStrategy.Aggregators)
            {
                yield return ToJson(snapshot[a.Percentile].ToString("0.###############"), _tagsByAggregator[a.Name], unixTimestamp);
            }
        }

        private Dictionary<double, double> GetSnapshot()
        {
            Heap<double> heap;
            double min;
            double max;
            int count;
            double sum;

            lock (_recordLock)
            {
                if (_count == 0) // there's no data to report if count == 0
                    return null;

                heap = _heap;
                if (heap != null)
                    _heap = new Heap<double>(_aggregatorStrategy.UseMaxHeap ? HeapMode.Max : HeapMode.Min);

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

            if (_trackMean)
                snapshot[-1.0] = sum/count;
            if (_trackMax)
                snapshot[1.0] = max;
            if (_trackMin)
                snapshot[0.0] = min;

            if (heap != null)
            {
                var heapLastIndex = heap.Count - 1;
                var extracted = 0;
                var percentiles = _aggregatorStrategy.Percentiles;
                var lastExtracted = Double.NaN;

                if (heap.HeapMode == HeapMode.Max)
                {
                    for (var i = percentiles.Count - 1; i > -1; i--)
                    {
                        double p = percentiles[i];
                        var index = (int) Math.Round((1.0 - p)*heapLastIndex);
                        for (; extracted <= index; extracted++)
                        {
                            lastExtracted = heap.Pop();
                        }
                        snapshot[p] = lastExtracted;
                    }
                }
                else
                {
                    foreach (var p in percentiles)
                    {
                        var index = (int) Math.Round(p*heapLastIndex);
                        for (; extracted <= index; extracted++)
                        {
                            lastExtracted = heap.Pop();
                        }
                        snapshot[p] = lastExtracted;
                    }
                }
            }

            return snapshot;
        }

        protected override string GetAggregatorTagName()
        {
            var attribute = GetType().GetCustomAttribute<GaugeAggregatorTagNameAttribute>();
            return attribute != null ? attribute.Name : "aggregator";
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

                var aggregators = GetType().GetCustomAttributes<GaugeAggregatorAttribute>().ToList().AsReadOnly();
                if (aggregators.Count == 0)
                    throw new Exception(GetType().FullName + " has no GaugeAggregator attributes. All gauges must have at least one.");

                var hash = new HashSet<string>();
                foreach (var r in aggregators)
                {
                    if (hash.Contains(r.Name))
                        throw new Exception(String.Format("{0} has more than one gauge aggregator with the name \"{1}\".", type.FullName, r.Name));
                }

                return _aggregatorsByTypeCache[type] = new GaugeAggregatorStrategy(aggregators);
            }
        }

        private class GaugeAggregatorStrategy
        {
            public readonly IReadOnlyCollection<GaugeAggregatorAttribute> Aggregators;

            public readonly bool UseMaxHeap;
            public readonly bool UseMinHeap;
            public readonly bool SpecialCaseMax;
            public readonly bool SpecialCaseMin;
            public readonly bool TrackMean;
            public readonly ReadOnlyCollection<double> Percentiles;

            public GaugeAggregatorStrategy(IReadOnlyCollection<GaugeAggregatorAttribute> aggregators)
            {
                Aggregators = aggregators;

                var percentiles = new List<double>();

                foreach (var r in aggregators)
                {
                    if (r.Percentile < 0)
                    {
                        TrackMean = true;
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

                if (percentiles.Count > 0)
                {
                    percentiles.Sort();

                    var from100 = 1.0 - percentiles.Last();
                    var from0 = percentiles[0];
                    if (from100 < from0 || (SpecialCaseMax && from100 == from0))
                    {
                        UseMaxHeap = true;
                        if (SpecialCaseMax)
                        {
                            SpecialCaseMax = false;
                            percentiles.Add(1.0);
                        }
                    }
                    else
                    {
                        UseMinHeap = true;
                        if (SpecialCaseMin)
                        {
                            SpecialCaseMin = false;
                            percentiles.Insert(0, 0.0);
                        }
                    }
                }

                Percentiles = percentiles.AsReadOnly();
            }
        }
    }
}
