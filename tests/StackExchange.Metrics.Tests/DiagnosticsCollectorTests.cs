using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Hosting;
using StackExchange.Metrics.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Metrics.Tests
{
    public class DiagnosticsCollectorTests
    {
        private readonly ITestOutputHelper _output;

        public DiagnosticsCollectorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task CounterCallbacksAreFired()
        {
            // spin up a diagnostics collector
            var diagnosticsCollector = new DiagnosticsCollector(new TestOutputLogger<DiagnosticsCollector>(_output));

            // listen for our custom event source
            diagnosticsCollector.AddSource(
                new EventPipeProvider(
                    CustomEventSource.SourceName,
                    EventLevel.LogAlways,
                    0,
                    arguments: new Dictionary<string, string>()
                    {
                        { "EventCounterIntervalSec", "1" }
                    }
                )
            );

            // add a callback for our counters
            var callCount = 0;
            var timeout = TimeSpan.FromSeconds(2);
            var callbackEvent = new AutoResetEvent(false);
            var receiveEvent = new AutoResetEvent(false);
            diagnosticsCollector.AddCounterCallback(
                CustomEventSource.SourceName,
                CustomEventSource.CounterName,
                v =>
                {
                    Interlocked.Increment(ref callCount);
                    callbackEvent.Set();
                    receiveEvent.WaitOne(timeout);
                }
            );

            // kick off the diagnostics collector
            await ((IHostedService)diagnosticsCollector).StartAsync(CancellationToken.None);

            // increment one of our counters to kick off event processing
            CustomEventSource.Instance.IncrementCounter();

            // wait until the collector starts processing events
            await diagnosticsCollector.WaitUntilProcessing();

            try
            {
                // we should have received one event here
                Assert.True(callbackEvent.WaitOne(timeout), "Did not receive initial counter metric value");
                Assert.Equal(1, callCount);
                receiveEvent.Set();

                // increment the counter
                CustomEventSource.Instance.IncrementCounter();

                // shoulda received another event!
                Assert.True(callbackEvent.WaitOne(timeout), "Did not receive updated counter metric value");
                Assert.Equal(2, callCount);
                receiveEvent.Set();
            }
            finally
            {
                await ((IHostedService) diagnosticsCollector).StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task GaugeCallbacksAreFired()
        {
            // spin up a diagnostics collector
            var diagnosticsCollector = new DiagnosticsCollector(new TestOutputLogger<DiagnosticsCollector>(_output));

            // listen for our custom event source
            diagnosticsCollector.AddSource(
                new EventPipeProvider(
                    CustomEventSource.SourceName,
                    EventLevel.LogAlways,
                    0,
                    arguments: new Dictionary<string, string>()
                    {
                        { "EventCounterIntervalSec", "1" }
                    }
                )
            );

            // add a callback for our counters
            var callCount = 0;
            var timeout = TimeSpan.FromSeconds(2);
            var callbackEvent = new AutoResetEvent(false);
            var receiveEvent = new AutoResetEvent(false);
            diagnosticsCollector.AddGaugeCallback(
                CustomEventSource.SourceName,
                CustomEventSource.GaugeName,
                v =>
                {
                    Interlocked.Increment(ref callCount);
                    callbackEvent.Set();
                    receiveEvent.WaitOne(timeout);
                }
            );

            // kick off the diagnostics collector
            await ((IHostedService)diagnosticsCollector).StartAsync(CancellationToken.None);

            // update the gauge to kick off event processing
            CustomEventSource.Instance.UpdateGauge();

            // wait until the collector starts processing events
            await diagnosticsCollector.WaitUntilProcessing();

            try
            {
                // we should have received one event here
                Assert.True(callbackEvent.WaitOne(timeout), "Did not receive initial gauge metric value");
                Assert.Equal(1, callCount);
                receiveEvent.Set();

                // update the gauge
                CustomEventSource.Instance.UpdateGauge();

                // shoulda received another event!
                Assert.True(callbackEvent.WaitOne(timeout), "Did not receive updated gauge metric value");
                Assert.Equal(2, callCount);
                receiveEvent.Set();
            }
            finally
            {
                await ((IHostedService) diagnosticsCollector).StopAsync(CancellationToken.None);
            }
        }

    }

    [EventSource(Name = SourceName)]
    public class CustomEventSource : EventSource
    {
        private readonly Random _rng;
        private readonly IncrementingEventCounter _counter;
        private readonly EventCounter _gauge;

        public const string SourceName = nameof(CustomEventSource);
        public const string CounterName = "custom-counter";
        public const string GaugeName = "custom-gauge";

        private static CustomEventSource _instance;

        public static CustomEventSource Instance { get; } = _instance ??= new CustomEventSource();

        public CustomEventSource() : base(nameof(CustomEventSource))
        {
            _rng = new Random();
            _counter = new IncrementingEventCounter(CounterName, this);
            _gauge = new EventCounter(GaugeName, this);
        }

        public void IncrementCounter() => _counter.Increment(1);

        public void UpdateGauge() => _gauge.WriteMetric(_rng.NextDouble() * 100);
    }
}
