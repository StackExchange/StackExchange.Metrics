using System;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial.Buffers;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;

namespace StackExchange.Metrics.Benchmarks
{
    [Config(typeof(BenchmarkConfig))]
    public class Readings
    {
        private BenchmarkSource[] _sources;
        private BufferWriter<byte> _buffer;
        private BufferWriterBatch _bufferBatch;
        private NoOpBatch _noOpBatch;

        private class BenchmarkSource : MetricSource
        {
            public Counter Counter { get; }
            public SamplingGauge Gauge { get; }
            public Counter<string> CounterWithTag { get; }
            public BenchmarkSource(MetricSourceOptions options) : base(options)
            {
                Counter = AddCounter("Test Counter", "Furlongs", "Invented by narwhals");
                Gauge = AddSamplingGauge("Test Gauge", "Furlongs", "Invented by narwhals");
                CounterWithTag = AddCounter("Counter with Tags", "Furlongs", "Invented by narwhals", new MetricTag<string>("String"));
            }
        }

        [GlobalSetup]
        public void Setup()
        {
            var options = new MetricSourceOptions();

            _buffer = BufferWriter<byte>.Create(8192);
            _bufferBatch = new BufferWriterBatch(_buffer);
            _noOpBatch = new NoOpBatch();
            _sources = new[]
            {
                new BenchmarkSource(options),
                new BenchmarkSource(options),
                new BenchmarkSource(options)
            };

            for (var i = 0; i < _sources.Length; i++)
            {
                _sources[i].CounterWithTag.Increment("A");
                _sources[i].CounterWithTag.Increment("B");
                _sources[i].CounterWithTag.Increment("C");
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _buffer.Dispose();
        }

        [Benchmark]
        public void WriteReadings_NoOp()
        {
            for (var i = 0; i < _sources.Length; i++)
            {
                _sources[i].Counter.Increment();
                _sources[i].Gauge.Record(10);
                _sources[i].CounterWithTag.Increment("A");
                _sources[i].CounterWithTag.Increment("B");
                _sources[i].CounterWithTag.Increment("C");
            }

            _sources.WriteReadings(_noOpBatch, DateTime.UtcNow);
            using (_buffer.Flush())
            {
            }
        }

        [Benchmark]
        public void GetReadings_NoOp()
        {
            for (var i = 0; i < _sources.Length; i++)
            {
                _sources[i].Counter.Increment();
                _sources[i].Gauge.Record(10);
                _sources[i].CounterWithTag.Increment("A");
                _sources[i].CounterWithTag.Increment("B");
                _sources[i].CounterWithTag.Increment("C");
            }

            _ = _sources.GetReadings(DateTime.UtcNow);
        }

        [Benchmark]
        public void WriteReadings_Json()
        {
            for (var i = 0; i < _sources.Length; i++)
            {
                _sources[i].Counter.Increment();
                _sources[i].Gauge.Record(10);
                _sources[i].CounterWithTag.Increment("A");
                _sources[i].CounterWithTag.Increment("B");
                _sources[i].CounterWithTag.Increment("C");
            }

            _sources.WriteReadings(_bufferBatch, DateTime.UtcNow);
            using (_buffer.Flush( ))
            {
            }
        }

        [Benchmark]
        public void GetReadings_Json()
        {
            for (var i = 0; i < _sources.Length; i++)
            {
                _sources[i].Counter.Increment();
                _sources[i].Gauge.Record(10);
                _sources[i].CounterWithTag.Increment("A");
                _sources[i].CounterWithTag.Increment("B");
                _sources[i].CounterWithTag.Increment("C");
            }

            var readings = _sources.GetReadings(DateTime.UtcNow);
            foreach (var reading in readings)
            {
                using (var utfWriter = new Utf8JsonWriter(_buffer))
                {
                    JsonSerializer.Serialize(utfWriter, reading);
                }
            }
            using (_buffer.Flush())
            {
            }
        }

        private class NoOpBatch : IMetricReadingBatch
        {
            public long BytesWritten => 0;

            public long MetricsWritten => 0;

            public void Add(in MetricReading reading)
            {
            }
        }

        private class BufferWriterBatch : IMetricReadingBatch
        {
            private readonly BufferWriter<byte> _buffer;

            public BufferWriterBatch(BufferWriter<byte> buffer)
            {
                _buffer = buffer;
            }

            public long BytesWritten => 0;

            public long MetricsWritten => 0;

            public void Add(in MetricReading reading)
            {
                using (var utfWriter = new Utf8JsonWriter(_buffer))
                {
                    JsonSerializer.Serialize(utfWriter, reading);
                }
            }
        }
    }
}
