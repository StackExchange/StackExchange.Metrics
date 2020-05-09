using System;
using System.Linq;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    public class CumulativeCounterTests : MetricBaseTests<CumulativeCounter, CumulativeCounter<string>, CumulativeCounter<string, TagValues>>
    {
        protected override CumulativeCounter CreateMetric(MetricSource source) => source.AddCumulativeCounter("test", "units", "desc");
        protected override void UpdateMetric(CumulativeCounter metric) => metric.Increment();
        protected override CumulativeCounter<string> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag) => source.AddCumulativeCounter("test", "units", "desc", tag);
        protected override void UpdateMetric(CumulativeCounter<string> metric, string tag) => metric.Increment(tag);
        protected override CumulativeCounter<string, TagValues> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag1, in MetricTag<TagValues> tag2) => source.AddCumulativeCounter("test", "units", "desc", tag1, tag2);
        protected override void UpdateMetric(CumulativeCounter<string, TagValues> metric, string tag1, TagValues tag2) => metric.Increment(tag1, tag2);

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
        public void IncrementMultiple_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateMetric(source);
            c.Increment();
            c.Increment();
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(2, reading.Value);
                }
            );
        }

        [Fact]
        public void Tagged_IncrementByOne_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag);
            c.Increment("A");
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
        public void Tagged_IncrementMultiple_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag);
            c.Increment("A");
            c.Increment("A");
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(2, reading.Value);
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
            c.Increment("A", TagValues.A);
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
        public void MultiTagged_IncrementMultiple_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateTaggedMetric(source, StringTag, EnumTag);
            c.Increment("A", TagValues.A);
            c.Increment("A", TagValues.A);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(2, reading.Value);
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
            c.Increment("A", TagValues.A);
            c.Increment("B", TagValues.A);
            c.Increment("A", TagValues.B);
            var readings = c.GetReadings(DateTime.UtcNow).OrderBy(x => string.Join(",", x.Tags.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}")));
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(2, reading.Value);
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
