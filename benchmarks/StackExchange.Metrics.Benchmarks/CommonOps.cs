using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;

namespace StackExchange.Metrics.Benchmarks
{
    [Config(typeof(BenchmarkConfig))]
    public class CommonOps
    {
        public MetricSource _source;
        public Counter _counter;
        public SamplingGauge _samplingGauge;
        private Counter<string> _counterWithStringTag;
        private Counter<int> _counterWithIntTag;
        private Counter<MyEnum> _counterWithEnumTag;

        public enum MyEnum
        {
            A, B, C
        };


        public IEnumerable<object[]> Iterations()
        {
            yield return new object[] { 1 };
            yield return new object[] { 2 };
            yield return new object[] { 5 };
        }

        [GlobalSetup]
        public void Setup()
        {
            _source = new MetricSource(new MetricSourceOptions());
            _counter = _source.AddCounter("Test Counter", "Furlongs", "Invented by narwhals");
            _samplingGauge = _source.AddSamplingGauge("Test Gauge", "Furlongs", "Invented by narwhals");
            _counterWithStringTag = _source.AddCounter("Counter with Tags", "Furlongs", "Invented by narwhals", new MetricTag<string>("String"));
            _counterWithIntTag = _source.AddCounter("Counter with Tags", "Furlongs", "Invented by narwhals", new MetricTag<int>("Int"));
            _counterWithEnumTag = _source.AddCounter("Counter with Tags", "Furlongs", "Invented by narwhals", new MetricTag<MyEnum>("MyEnum"));
        }

        [Benchmark]
        public void CounterIncrement() => _counter.Increment();

        [Benchmark]
        public void SamplingSet() => _samplingGauge.Record(12);

        [Benchmark]
        [ArgumentsSource(nameof(Iterations))]
        public void Tagged_CounterIncrement_String_One(int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                _counterWithStringTag.Increment("A");
            }
        }

        [Benchmark]
        [ArgumentsSource(nameof(Iterations))]
        public void Tagged_CounterIncrement_String_Many(int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                _counterWithStringTag.Increment("A");
                _counterWithStringTag.Increment("B");
                _counterWithStringTag.Increment("C");
            }
        }

        [Benchmark]
        [ArgumentsSource(nameof(Iterations))]
        public void Tagged_CounterIncrement_Int_One(int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                _counterWithIntTag.Increment(1);
            }
        }

        [Benchmark]
        [ArgumentsSource(nameof(Iterations))]
        public void Tagged_CounterIncrement_Int_Many(int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                _counterWithIntTag.Increment(1);
                _counterWithIntTag.Increment(2);
                _counterWithIntTag.Increment(3);
            }
        }

        [Benchmark]
        [ArgumentsSource(nameof(Iterations))]
        public void Tagged_CounterIncrement_Enum(int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                _counterWithEnumTag.Increment(MyEnum.A);
            }
        }

        [Benchmark]
        [ArgumentsSource(nameof(Iterations))]
        public void Tagged_CounterIncrement_Enum_Many(int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                _counterWithEnumTag.Increment(MyEnum.A);
                _counterWithEnumTag.Increment(MyEnum.B);
                _counterWithEnumTag.Increment(MyEnum.C);
            }
        }
    }
}
