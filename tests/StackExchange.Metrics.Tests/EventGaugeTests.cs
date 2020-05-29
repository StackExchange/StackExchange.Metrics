using System;
using System.Linq;
using StackExchange.Metrics.Metrics;
using Xunit;

namespace StackExchange.Metrics.Tests
{

    public class EventGaugeTests : MetricBaseTests<EventGauge, EventGauge<string>, EventGauge<string, TagValues>>
    {
        protected override EventGauge CreateMetric(MetricSource source) => source.AddEventGauge("test", "units", "desc");
        protected override void UpdateMetric(EventGauge metric) => metric.Record(42.123d);
        protected override EventGauge<string> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag) => source.AddEventGauge("test", "units", "desc", tag);
        protected override void UpdateMetric(EventGauge<string> metric, string tag) => metric.Record(tag, 42.123d);
        protected override EventGauge<string, TagValues> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag1, in MetricTag<TagValues> tag2) => source.AddEventGauge("test", "units", "desc", tag1, tag2);
        protected override void UpdateMetric(EventGauge<string, TagValues> metric, string tag1, TagValues tag2) => metric.Record(tag1, tag2, 42.123d);

        [Fact]
        public void Record_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateMetric(source);
            g.Record(42.4534);
            var readings = g.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(42.4534, reading.Value);
                }
            );
        }

        [Fact]
        public void MultipleRecord_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateMetric(source);
            g.Record(42.4534);
            g.Record(21.2267, DateTime.UtcNow.AddMilliseconds(10));
            var readings = g.GetReadings(DateTime.UtcNow).OrderBy(x => x.Timestamp);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(42.4534, reading.Value);
                },
                reading =>
                {
                    Assert.Equal(21.2267, reading.Value);
                }
            );
        }

        [Fact]
        public void Tagged_Record_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateTaggedMetric(source, StringTag);
            g.Record("A", 42.4534);
            var readings = g.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(42.4534, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact]
        public void Tagged_MultipleRecord_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateTaggedMetric(source, StringTag);
            g.Record("A", 42.4534);
            g.Record("A", 21.2267, DateTime.UtcNow.AddMilliseconds(10));
            var readings = g.GetReadings(DateTime.UtcNow).OrderBy(x => x.Timestamp);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(42.4534, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(21.2267, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact]
        public void Tagged_MultipleRecord_DifferentTags_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateTaggedMetric(source, StringTag);
            g.Record("A", 42.4534);
            g.Record("B", 21.2267);
            var readings = g.GetReadings(DateTime.UtcNow).OrderBy(x => string.Join(",", x.Tags.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}")));
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(42.4534, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(21.2267, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                }
            );
        }

        [Fact]
        public void MultiTagged_Record_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateTaggedMetric(source, StringTag, EnumTag);
            g.Record("A", TagValues.A, 42.4534);
            var readings = g.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(42.4534, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact]
        public void MultiTagged_MultipleRecord_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateTaggedMetric(source, StringTag, EnumTag);
            g.Record("A", TagValues.A, 42.4534);
            g.Record("A", TagValues.A, 21.2267, DateTime.UtcNow.AddMilliseconds(10));
            var readings = g.GetReadings(DateTime.UtcNow).OrderBy(x => x.Timestamp);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(42.4534, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(21.2267, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                }
            );
        }

        [Fact]
        public void MultiTagged_MultipleRecord_DifferentTags_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateTaggedMetric(source, StringTag, EnumTag);
            g.Record("A", TagValues.A, 42.4534);
            g.Record("A", TagValues.A, 21.2267, DateTime.UtcNow.AddMilliseconds(10));
            g.Record("B", TagValues.A, 21.2267);
            g.Record("A", TagValues.B, 10.61335);
            var readings = g.GetReadings(DateTime.UtcNow).OrderBy(x => string.Join(",", x.Tags.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"))).ThenBy(x => x.Timestamp);
            Assert.Collection(
                readings,
                reading =>
                {
                    Assert.Equal(42.4534, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(21.2267, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    Assert.Equal(21.2267, reading.Value);
                    Assert.Collection(
                        reading.Tags.OrderBy(x => x.Key).ThenBy(x => x.Value),
                        tag => AssertTagEqual(options, tag, EnumTag, TagValues.A),
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                },
                reading =>
                {
                    Assert.Equal(10.61335, reading.Value);
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
