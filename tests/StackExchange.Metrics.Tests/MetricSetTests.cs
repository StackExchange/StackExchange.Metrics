using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    public class MetricSetTests
    {
        [Fact]
        public async Task MetricSetsAreReported()
        {
            var metricSet = new TestMetricSet();
            var collector = new MetricsCollector(
                new MetricsCollectorOptions
                {
                    Endpoints = new []
                    {
                        new MetricEndpoint("Local", new LocalMetricHandler()),
                    },
                    Sets = new[] { metricSet },
                    RetryInterval = TimeSpan.Zero,
                    SnapshotInterval = TimeSpan.FromMilliseconds(20),
                }
            );

            try
            {
                // make sure initialization happened
                await Task.WhenAny(metricSet.InitializeTask, Task.Delay(100));
                Assert.True(metricSet.InitializeTask.IsCompleted, "Metric set was not initialized");

                // and then make sure we actually got snapshotted!
                await Task.WhenAny(metricSet.SnapshotTask, Task.Delay(100));
                Assert.True(metricSet.SnapshotTask.IsCompleted, "Metric set was not snapshotted");
            }
            finally
            {
                collector.Shutdown();
            }
        }

        [Fact]
        public void ExceptionInInitialize_Throws()
        {
            var metricSet = new FailureMetricSet(throwOnInitialize: true);

            Assert.Throws<Exception>(
                () =>
                {
                    new MetricsCollector(
                        new MetricsCollectorOptions
                        {
                            Endpoints = new[] {new MetricEndpoint("Local", new LocalMetricHandler()),},
                            Sets = new[] {metricSet},
                            RetryInterval = TimeSpan.Zero,
                            SnapshotInterval = TimeSpan.FromMilliseconds(20),
                        }
                    );
                }
            );
        }

        [Fact]
        public async Task ExceptionInSnapshot_IsHandled()
        {
            var exceptionThrown = new ManualResetEventSlim(false);
            var metricSet = new FailureMetricSet(throwOnSnapshot: true);
            var collector = new MetricsCollector(
                new MetricsCollectorOptions
                {
                    Endpoints = new[] {new MetricEndpoint("Local", new LocalMetricHandler()),},
                    Sets = new[] {metricSet},
                    RetryInterval = TimeSpan.Zero,
                    SnapshotInterval = TimeSpan.FromMilliseconds(20),
                    ExceptionHandler = ex => exceptionThrown.Set()
                }
            );

            Assert.True(exceptionThrown.Wait(TimeSpan.FromSeconds(1)), "Exception handler was not invoked");
        }

        private class TestMetricSet : IMetricSet
        {
            private readonly TaskCompletionSource<object> _initializeTask;
            private readonly TaskCompletionSource<object> _snapshotTask;

            public Task InitializeTask => _initializeTask.Task;

            public Task SnapshotTask => _snapshotTask.Task;

            public TestMetricSet()
            {
                _initializeTask = new TaskCompletionSource<object>();
                _snapshotTask = new TaskCompletionSource<object>();
            }

            public void Initialize(IMetricsCollector collector) => _initializeTask.TrySetResult(null);

            public void Snapshot() => _snapshotTask.TrySetResult(null);
        }

        private class FailureMetricSet : IMetricSet
        {
            private readonly bool _throwOnInitialize;
            private readonly bool _throwOnSnapshot;

            public FailureMetricSet(bool throwOnInitialize = false, bool throwOnSnapshot = false)
            {
                _throwOnInitialize = throwOnInitialize;
                _throwOnSnapshot = throwOnSnapshot;
            }

            public void Initialize(IMetricsCollector collector)
            {
                if (_throwOnInitialize) throw new Exception("Arghhhhh! Initialization blew up!");
            }

            public void Snapshot()
            {
                if (_throwOnSnapshot) throw new Exception("Arghhhhh! Snapshot blew up!");
            }
        }
    }
}
