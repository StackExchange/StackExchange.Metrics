using System;
using System.Linq;
using StackExchange.Metrics.Metrics;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    public class CounterTests : MetricBaseTests<Counter, Counter<string>, Counter<string, TagValues>>
    {
        protected override Counter CreateMetric(MetricSource source) => source.AddCounter("test", "units", "desc");
        protected override void UpdateMetric(Counter metric) => metric.Increment();
        protected override Counter<string> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag) => source.AddCounter("test", "units", "desc", tag);
        protected override void UpdateMetric(Counter<string> metric, string tag) => metric.Increment(tag);
        protected override Counter<string, TagValues> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag1, in MetricTag<TagValues> tag2) => source.AddCounter("test", "units", "desc", tag1, tag2);
        protected override void UpdateMetric(Counter<string, TagValues> metric, string tag1, TagValues tag2) => metric.Increment(tag1, tag2);

        [Fact]
        public void NoIncrement_HasNoReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateMetric(source);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Empty(readings);
        }

        [Fact]
        public void IncrementByOne_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateMetric(source);
            c.Increment();
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
        public void IncrementByTen_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateMetric(source);
            c.Increment(10);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(10, reading.Value);
                }
            );
        }

        [Fact]
        public void IncrementMultiple_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateMetric(source);
            c.Increment();
            c.Increment(2);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(3, reading.Value);
                }
            );
        }

        [Fact]
        public void Tagged_IncrementByOne_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag);
            c.Increment("A", 1);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(1, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact]
        public void Tagged_IncrementByTen_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag);
            c.Increment("A", 10);
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

        [Fact]
        public void Tagged_IncrementMultiple_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag);
            c.Increment("A");
            c.Increment("A", 2);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(3, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact]
        public void Tagged_IncrementMultiple_DifferentTags_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag);
            c.Increment("A");
            c.Increment("B");
            var readings = c.GetReadings(DateTime.UtcNow).OrderBy(x => string.Join(",", x.Tags.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}")));
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(1, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(1, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                }
            );
        }

        [Fact]
        public void MultiTagged_IncrementByOne_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag, EnumTag);
            c.Increment("A", TagValues.A, 1);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(1, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact]
        public void MultiTagged_IncrementByTen_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag, EnumTag);
            c.Increment("A", TagValues.A, 10);
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

        [Fact]
        public void MultiTagged_IncrementMultiple_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag, EnumTag);
            c.Increment("A", TagValues.A);
            c.Increment("A", TagValues.A, 2);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(3, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact]
        public void MultiTagged_IncrementMultiple_DifferentTags_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag, EnumTag);
            c.Increment("A", TagValues.A);
            c.Increment("A", TagValues.A, 2);
            c.Increment("B", TagValues.A);
            c.Increment("A", TagValues.B);
            var readings = c.GetReadings(DateTime.UtcNow).OrderBy(x => string.Join(",", x.Tags.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}")));
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(3, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(1, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                },
                reading =>
                {
                    Assert.Equal(1, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.B),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }
    }
}
