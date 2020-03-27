using BenchmarkDotNet.Attributes;
using StackExchange.Metrics;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Metrics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Scratch
{
    [MemoryDiagnoser]
    public class Benchmark
    {
        CancellationTokenSource _cancellationTokenSource;
        MetricsCollector _emptyCollector;
        MetricsCollector _httpCollector;
        MetricsCollector _udpCollector;

        [GlobalSetup]
        public void SetUp()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            var emptyOptions = new MetricsCollectorOptions
            {
                ExceptionHandler = ex => Console.WriteLine(ex),
                Endpoints = Array.Empty<MetricEndpoint>(),
                DefaultTags = new Dictionary<string, string> {
                    { "host", NameTransformers.Sanitize(Environment.MachineName.ToLower()) }
                }.ToImmutableDictionary(),
                MetricsNamePrefix = "benchmark1",
                SnapshotInterval = TimeSpan.FromSeconds(1),
                ThrowOnQueueFull = false,
            };

            var httpOptions = new MetricsCollectorOptions
            {
                ExceptionHandler = ex => Console.WriteLine(ex),
                Endpoints = new[]
                {
                    new MetricEndpoint("Benchmark", new SignalFxMetricHandler(new Uri("http://127.0.0.1/")))
                },
                DefaultTags = new Dictionary<string, string> {
                    { "host", NameTransformers.Sanitize(Environment.MachineName.ToLower()) }
                }.ToImmutableDictionary(),
                MetricsNamePrefix = "benchmark1",
                SnapshotInterval = TimeSpan.FromSeconds(1),
                ThrowOnQueueFull = false,
            };

            var udpOptions = new MetricsCollectorOptions
            {
                ExceptionHandler = ex => Console.WriteLine(ex),
                Endpoints = new[]
                {
                    new MetricEndpoint("Benchmark", new TestUdpMetricHandler(_cancellationTokenSource.Token) { MaxPayloadSize = 100 })
                },
                DefaultTags = new Dictionary<string, string> {
                    { "host", NameTransformers.Sanitize(Environment.MachineName.ToLower()) }
                }.ToImmutableDictionary(),
                MetricsNamePrefix = "benchmark1",
                SnapshotInterval = TimeSpan.FromSeconds(1),
                ThrowOnQueueFull = false,
            };

            _emptyCollector = new MetricsCollector(emptyOptions);
            _httpCollector = new MetricsCollector(httpOptions);
            _udpCollector = new MetricsCollector(udpOptions);
        }

        [GlobalCleanup]
        public void CleanUp()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _emptyCollector.Shutdown();
            _httpCollector.Shutdown();
            _udpCollector.Shutdown();
        }

        [Benchmark(Baseline = true)]
        public async Task JustSerialization()
        {
            var counter = _emptyCollector.GetMetric<Counter>("counter", "ms", "Testing 1,2,3");
            var gauge = _emptyCollector.GetMetric<EventGauge>("gauge", "units", "Testing 1,2,3");
            var timeout = TimeSpan.FromSeconds(2);
            var start = DateTime.UtcNow;
            var value = 0.23d;
            while (DateTime.UtcNow - start < timeout)
            {
                counter.Increment();
                gauge.Record(value++);
                await Task.Delay(10);
            }
        }

        //[Benchmark]
        //public async Task Http()
        //{
        //    var counter = _httpCollector.GetMetric<Counter>("counter", "ms", "Testing 1,2,3");
        //    var gauge = _httpCollector.GetMetric<EventGauge>("gauge", "units", "Testing 1,2,3");
        //    var timeout = TimeSpan.FromSeconds(2);
        //    var start = DateTime.UtcNow;
        //    var value = 0.23d;
        //    while (DateTime.UtcNow - start < timeout)
        //    {
        //        counter.Increment();
        //        gauge.Record(value++);
        //        await Task.Delay(10);
        //    }
        //}

        [Benchmark]
        public async Task Udp()
        {
            var counter = _udpCollector.GetMetric<Counter>("counter", "ms", "Testing 1,2,3");
            var gauge = _udpCollector.GetMetric<EventGauge>("gauge", "units", "Testing 1,2,3");
            var timeout = TimeSpan.FromSeconds(2);
            var start = DateTime.UtcNow;
            var value = 0.23d;
            while (DateTime.UtcNow - start < timeout)
            {
                counter.Increment();
                gauge.Record(value++);
                await Task.Delay(10);
            }
        }
    }
}
