using System;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    public class MetricSourceTests
    {
        [Fact]
        public async Task MetricSourcesAreAttachedAndDetached()
        {
            var metricSource = new TestMetricSource();
            var collector = new MetricsCollector(
                new MetricsCollectorOptions
                {
                    Endpoints = new []
                    {
                        new MetricEndpoint("Local", new LocalMetricHandler()),
                    },
                    Sources = new[] { metricSource },
                    RetryInterval = TimeSpan.Zero,
                    SnapshotInterval = TimeSpan.FromMilliseconds(20),
                }
            );

            collector.Start();

            try
            {
                // make sure initialization happened
                await Task.WhenAny(metricSource.AttachTask, Task.Delay(1000));
                Assert.True(metricSource.AttachTask.IsCompleted, "Metric source was not attached");

                // make sure we got snapshotted
                await Task.WhenAny(metricSource.SnapshotTask, Task.Delay(1000));
                Assert.True(metricSource.SnapshotTask.IsCompleted, "Metric source was not snapshotted");
            }
            finally
            {
                collector.Stop();

                // make sure we got detached
                await Task.WhenAny(metricSource.DetachTask, Task.Delay(1000));
                Assert.True(metricSource.DetachTask.IsCompleted, "Metric source was not detached");
            }
        }

        [Fact]
        public void ExceptionInAttach_Throws()
        {
            var source = new FailureMetricSource(throwOnAttach: true);
            var collector = new MetricsCollector(
                new MetricsCollectorOptions
                {
                    Endpoints = new[] { new MetricEndpoint("Local", new LocalMetricHandler()), },
                    Sources = new[] { source },
                    RetryInterval = TimeSpan.Zero,
                    SnapshotInterval = TimeSpan.FromMilliseconds(20),
                }
            );

            Assert.Throws<Exception>(() => collector.Start());
        }

        [Fact]
        public void ExceptionInDetach_Throws()
        {
            var source = new FailureMetricSource(throwOnDetach: true);
            var collector = new MetricsCollector(
                new MetricsCollectorOptions
                {
                    Endpoints = new[] {new MetricEndpoint("Local", new LocalMetricHandler()),},
                    Sources = new[] {source},
                    RetryInterval = TimeSpan.Zero,
                    SnapshotInterval = TimeSpan.FromMilliseconds(20),
                }
            );

            collector.Start();

            Assert.Throws<Exception>(() => collector.Stop());
        }

        [Fact]
        public void GetReadings_EnumeratesAllUntaggedMetrics()
        {
            var source = new TestMetricSource();
            var counter = source.AddCounter("counter", "unit", "desc");
            var gauge = source.AddSamplingGauge("gauge", "unit", "desc");
            counter.Increment();
            counter.Increment();
            gauge.Record(42.123d);
            var readings = source.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal("counter", reading.Name);
                    Assert.Equal(2, reading.Value);
                },
                reading =>
                {
                    Assert.Equal("gauge", reading.Name);
                    Assert.Equal(42.123d, reading.Value);
                });
        }

        [Fact]
        public void GetReadings_EnumeratesAllTaggedMetrics()
        {
            var source = new TestMetricSource();
            var counter = source.AddCounter("counter", "unit", "desc", new MetricTag<string>("tag"));
            counter.Increment("test");
            counter.Increment("test_2");
            counter.Increment("test_2");
            counter.Increment("test_3");
            counter.Increment("test_3");
            counter.Increment("test_3");
            // HACK: need to order by the reported value to ensure we get consistent order
            var readings = source.GetReadings(DateTime.UtcNow).OrderBy(x => x.Value);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal("counter", reading.Name);
                    Assert.Equal(1, reading.Value);
                    Assert.True(reading.Tags.TryGetValue("tag", out var value) && value == "test");
                },
                reading =>
                {
                    Assert.Equal("counter", reading.Name);
                    Assert.Equal(2, reading.Value);
                    Assert.True(reading.Tags.TryGetValue("tag", out var value) && value == "test_2");
                },
                reading =>
                {
                    Assert.Equal("counter", reading.Name);
                    Assert.Equal(3, reading.Value);
                    Assert.True(reading.Tags.TryGetValue("tag", out var value) && value == "test_3");
                });
        }

        private class FailureMetricSource : MetricSource
        {
            private readonly bool _throwOnAttach;
            private readonly bool _throwOnDetach;

            public FailureMetricSource(bool throwOnAttach = false, bool throwOnDetach = false) : base(TestCreationOptions.Value)
            {
                _throwOnAttach = throwOnAttach;
                _throwOnDetach = throwOnDetach;
            }

            public override void Attach(IMetricsCollector collector)
            {
                if (_throwOnAttach) throw new Exception("Arghhhhh! Attach blew up!");
            }

            public override void Detach(IMetricsCollector collector)
            {
                if (_throwOnDetach) throw new Exception("Arghhhhh! Detach blew up!");
            }
        }
    }
}
