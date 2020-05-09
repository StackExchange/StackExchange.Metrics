using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    public class SnapshotGaugeTests : MetricBaseTests<SnapshotGauge, SnapshotGauge<string>, SnapshotGauge<string, TagValues>>
    {
        protected override SnapshotGauge CreateMetric(MetricSource source) => source.AddSnapshotGauge(Snapshot, "test", "units", "desc");
        protected override void UpdateMetric(SnapshotGauge metric) => _snapshotValues.Enqueue(42.4342);
        protected override SnapshotGauge<string> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag) => source.AddSnapshotGauge(Snapshot, "test", "units", "desc", tag);
        protected override void UpdateMetric(SnapshotGauge<string> metric, string tag) => UpdateMetric(metric.Get(tag));
        protected override SnapshotGauge<string, TagValues> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag1, in MetricTag<TagValues> tag2) => source.AddSnapshotGauge(Snapshot, "test", "units", "desc", tag1, tag2);
        protected override void UpdateMetric(SnapshotGauge<string, TagValues> metric, string tag1, TagValues tag2) => metric.Get(tag1, tag2);

        private readonly Queue<double?> _snapshotValues = new Queue<double?>();
        private double? Snapshot()
        {
            if (_snapshotValues.Count == 0)
            {
                return null;
            }

            return _snapshotValues.Dequeue();
        }

        [Fact]
        public void Snapshot_Null_HasNoReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateMetric(source);
            _snapshotValues.Clear();
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Empty(readings);
        }

        [Fact]
        public void Snapshot_NaN_HasNoReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateMetric(source);
            _snapshotValues.Enqueue(double.NaN);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Empty(readings);
        }

        [Fact]
        public void Snapshot_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateMetric(source);
            _snapshotValues.Enqueue(1);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(1, reading.Value);
                }
            );
        }

        [Fact]
        public void Tagged_Snapshot_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag);
            _ = c.Get("A");
            _snapshotValues.Enqueue(10);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(10, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact(Skip = "Snapshot values do not come out in order due to an implementation detail of how TaggedMetricFactory.GetMetrics() orders metrics. That means tags do not match up to values :(")]
        public void Tagged_Snapshot_DifferentTags_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag);
            _ = c.Get("A");
            _snapshotValues.Enqueue(10);
            _ = c.Get("B");
            _snapshotValues.Enqueue(20);
            var readings = c.GetReadings(DateTime.UtcNow).OrderBy(x => x.Value);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(10, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(20, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                }
            );
        }

        [Fact]
        public void MultiTagged_Snapshot_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag, EnumTag);
            _ = c.Get("A", TagValues.A);
            _snapshotValues.Enqueue(10);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(10, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact(Skip = "Snapshot values do not come out in order due to an implementation detail of how TaggedMetricFactory.GetMetrics() orders metrics. That means tags do not match up to values :(")]
        public void MultiTagged_Snapshot_DifferentTags_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag, EnumTag);
            _ = c.Get("A", TagValues.A);
            _snapshotValues.Enqueue(10);
            _ = c.Get("B", TagValues.A);
            _snapshotValues.Enqueue(20);
            _ = c.Get("A", TagValues.B);
            _snapshotValues.Enqueue(30);
            var readings = c.GetReadings(DateTime.UtcNow).OrderBy(x => x.Value);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(10, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(20, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.B),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(30, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                }
            );
        }
    }
}
