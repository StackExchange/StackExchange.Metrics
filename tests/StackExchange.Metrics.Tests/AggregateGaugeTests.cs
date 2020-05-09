using System;
using System.Linq;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;
using Xunit;

namespace StackExchange.Metrics.Tests
{

    public class AggregateGaugeTests : MetricBaseTests<AggregateGauge, AggregateGauge<string>, AggregateGauge<string, TagValues>>
    {
        protected override AggregateGauge CreateMetric(MetricSource source) => source.AddAggregateGauge("test", "units", "desc");
        protected override void UpdateMetric(AggregateGauge metric) => metric.Record(42.123d);
        protected override AggregateGauge<string> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag) => source.AddAggregateGauge("test", "units", "desc", tag);
        protected override void UpdateMetric(AggregateGauge<string> metric, string tag) => metric.Record(tag, 42.123d);
        protected override AggregateGauge<string, TagValues> CreateTaggedMetric(MetricSource source, in MetricTag<string> tag1, in MetricTag<TagValues> tag2) => source.AddAggregateGauge("test", "units", "desc", tag1, tag2);
        protected override void UpdateMetric(AggregateGauge<string, TagValues> metric, string tag1, TagValues tag2) => metric.Record(tag1, tag2, 42.123d);

        [Fact]
        public override void GetMetadata_ReturnsData()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var metric = CreateMetric(source);
            var metadata = metric.GetMetadata().OrderBy(x => x.Metric).ThenBy(x => x.Name).ToList();
            var suffixes = new[] { "_95", "_99", "_avg", "_count", "_max", "_median", "_min" };
            var descriptions = new[] { " (95th percentile)", " (99th percentile)", " (average)", " (count of the number of events recorded)", " (maximum)", " (median)", " (minimum)" };
            for (var i = 0; i < suffixes.Length; i++)
            {
                var startIndex = ((i + 1) * 3) - 3;
                var description = metadata[startIndex];
                var rate = metadata[startIndex + 1];
                var unit = metadata[startIndex + 2];
                var nameWithSuffix = metric.Name + suffixes[i];
                var descriptionForSuffix = metric.Description + descriptions[i];

                Assert.Equal(nameWithSuffix, rate.Metric);
                Assert.Equal(MetadataNames.Rate, rate.Name);
                Assert.Equal("gauge", rate.Value);

                Assert.Equal(nameWithSuffix, description.Metric);
                Assert.Equal(MetadataNames.Description, description.Name);
                Assert.Equal(descriptionForSuffix, description.Value);

                Assert.Equal(nameWithSuffix, unit.Metric);
                Assert.Equal(MetadataNames.Unit, unit.Name);
                Assert.Equal(metric.Unit, unit.Value);
            }
        }

        [Fact]
        public override void GetReadings_Resets()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateMetric(source);
            UpdateMetric(g);
            var readings = g.GetReadings(DateTime.UtcNow);
            Assert.NotEmpty(readings);
            readings = g.GetReadings(DateTime.UtcNow);
            // a completely reset aggregate gauge reports just the count
            Assert.Collection(
                readings,
                reading =>
                {
                    // count
                    Assert.Equal(g.Name + "_count", reading.Name);
                    Assert.Equal(0, reading.Value);
                }
            );
        }

        [Fact]
        public void Record_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateMetric(source);
            g.Record(2.4);
            g.Record(1.2);
            g.Record(4.8);
            g.Record(38.4);
            g.Record(19.2);
            g.Record(9.6);
            // total: 75.8, count: 6, mean: 12.6, max: 38.4, min: 1.2, median: 4.8, 95%: 38.4, 99%: 38.4
            var readings = g.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings.OrderBy(x => x.Name),
                reading =>
                {
                    // 95%
                    Assert.Equal(g.Name + "_95", reading.Name);
                    Assert.Equal(38.4, reading.Value);
                },
                reading =>
                {
                    // 99%
                    Assert.Equal(g.Name + "_99", reading.Name);
                    Assert.Equal(38.4, reading.Value);
                },
                reading =>
                {
                    // mean
                    Assert.Equal(g.Name + "_avg", reading.Name);
                    Assert.Equal(12.6, reading.Value);
                },
                reading =>
                {
                    // count
                    Assert.Equal(g.Name + "_count", reading.Name);
                    Assert.Equal(6, reading.Value);
                },
                reading =>
                {
                    // max
                    Assert.Equal(g.Name + "_max", reading.Name);
                    Assert.Equal(38.4, reading.Value);
                },
                reading =>
                {
                    // median
                    Assert.Equal(g.Name + "_median", reading.Name);
                    Assert.Equal(4.8, reading.Value);
                },
                reading =>
                {
                    // min
                    Assert.Equal(g.Name + "_min", reading.Name);
                    Assert.Equal(1.2, reading.Value);
                }
            );
        }

        [Fact]
        public void NoRecord_HasCountReading()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateMetric(source);
            var readings = g.GetReadings(DateTime.UtcNow);
            Assert.Collection(
                readings.OrderBy(x => x.Name),
                reading =>
                {
                    // count
                    Assert.Equal(g.Name + "_count", reading.Name);
                    Assert.Equal(0, reading.Value);
                }
            );
        }

        [Fact]
        public void Tagged_Record_HasValidReadings()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var g = CreateTaggedMetric(source, StringTag);
            g.Record("A", 2.4);
            g.Record("A", 1.2);
            g.Record("A", 4.8);
            g.Record("A", 38.4);
            g.Record("A", 19.2);
            g.Record("A", 9.6);

            g.Record("B", 2.4);
            g.Record("B", 4.8);
            g.Record("B", 1.2);
            // A: total: 75.8, count: 6, mean: 12.6, max: 38.4, min: 1.2, median: 4.8, 95%: 38.4, 99%: 38.4
            // B: total: 8.4, count: 3, mean: 2.8, max: 4.8, min: 1.2, median: 2.4, 95%: 4.8, 99%: 4.8
            var readings = g.GetReadings(DateTime.UtcNow).OrderBy(x => string.Join(",", x.Tags.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"))).ThenBy(x => x.Name);
            Assert.Collection(
                readings,
                reading =>
                {
                    // 95%
                    Assert.Equal(g.Name + "_95", reading.Name);
                    Assert.Equal(38.4, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    // 99%
                    Assert.Equal(g.Name + "_99", reading.Name);
                    Assert.Equal(38.4, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    // mean
                    Assert.Equal(g.Name + "_avg", reading.Name);
                    Assert.Equal(12.6, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    // count
                    Assert.Equal(g.Name + "_count", reading.Name);
                    Assert.Equal(6, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    // max
                    Assert.Equal(g.Name + "_max", reading.Name);
                    Assert.Equal(38.4, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    // median
                    Assert.Equal(g.Name + "_median", reading.Name);
                    Assert.Equal(4.8, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    // min
                    Assert.Equal(g.Name + "_min", reading.Name);
                    Assert.Equal(1.2, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "A")
                    );
                },
                reading =>
                {
                    // 95%
                    Assert.Equal(g.Name + "_95", reading.Name);
                    Assert.Equal(4.8, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                },
                reading =>
                {
                    // 99%
                    Assert.Equal(g.Name + "_99", reading.Name);
                    Assert.Equal(4.8, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                },
                reading =>
                {
                    // mean
                    Assert.Equal(g.Name + "_avg", reading.Name);
                    // yearp, double is weird with precision - should be 2.8 but is 2.799999999999
                    Assert.Equal(2.7999999999999994, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                },
                reading =>
                {
                    // count
                    Assert.Equal(g.Name + "_count", reading.Name);
                    Assert.Equal(3, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                },
                reading =>
                {
                    // max
                    Assert.Equal(g.Name + "_max", reading.Name);
                    Assert.Equal(4.8, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                },
                reading =>
                {
                    // median
                    Assert.Equal(g.Name + "_median", reading.Name);
                    Assert.Equal(2.4, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                },
                reading =>
                {
                    // min
                    Assert.Equal(g.Name + "_min", reading.Name);
                    Assert.Equal(1.2, reading.Value);
                    Assert.Collection(
                        reading.Tags,
                        tag => AssertTagEqual(options, tag, StringTag, "B")
                    );
                }
            );
        }
    }
}
