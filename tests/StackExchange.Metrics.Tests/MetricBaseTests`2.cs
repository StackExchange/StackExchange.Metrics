using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Metrics.Infrastructure;
using Xunit;

namespace StackExchange.Metrics.Tests
{

    public abstract class MetricBaseTests<TMetric, TTaggedMetric, TMultiTaggedMetric>
        where TMetric : MetricBase
        where TTaggedMetric : TaggedMetricFactory<TMetric, string>
        where TMultiTaggedMetric : TaggedMetricFactory<TMetric, string, TagValues>
    {
        protected static readonly MetricTag<string> StringTag = new MetricTag<string>("string");
        protected static readonly MetricTag<TagValues> EnumTag = new MetricTag<TagValues>("enum");

        [Fact]
        public void Constructor_FailedTagNameValidation()
        {
            var options = new MetricSourceOptions
            {
                TagNameValidator = _ => false
            };

            var source = new MetricSource(options);
            Assert.Throws<ArgumentException>("tag", () => CreateTaggedMetric(source, StringTag));
        }

        [Fact]
        public void Constructor_FailedTagValueValidation()
        {
            var options = new MetricSourceOptions
            {
                TagValueValidator = _ => false
            };

            var source = new MetricSource(options);
            var metric = CreateTaggedMetric(source, StringTag);
            Assert.Throws<ArgumentException>("value", () => UpdateMetric(metric, "test"));
        }

        [Fact]
        public void Constructor_FailedMetricNameValidation_Untagged()
        {
            var options = new MetricSourceOptions
            {
                MetricNameValidator = _ => false
            };

            var source = new MetricSource(options);
            Assert.Throws<ArgumentException>("name", () => CreateMetric(source));
        }

        [Fact]
        public void Constructor_CanOverrideDefaultTags()
        {
            var options = new MetricSourceOptions();
            options.DefaultTags.Clear();
            options.DefaultTags[StringTag.Name] = "default_value";

            // create a metric and make sure the tags on its readings
            // do not have the default tag value
            var source = new MetricSource(options);
            var metric = CreateTaggedMetric(source, StringTag);
            UpdateMetric(metric, "value");
            var readings = metric.GetReadings(DateTime.UtcNow).ToList();
            Assert.NotEmpty(readings);
            var reading = readings[0];
            Assert.Collection(
                reading.Tags,
                tag =>
                {
                    var transformedKey = options.TagNameTransformer(StringTag.Name);
                    var transformedValue = options.TagValueTransformer(transformedKey, "value");

                    Assert.Equal(transformedKey, tag.Key);
                    Assert.Equal(transformedValue, tag.Value);
                });
        }

        [Fact]
        public void Constructor_FailedMetricNameValidation_Tagged()
        {
            var options = new MetricSourceOptions
            {
                MetricNameValidator = _ => false
            };

            var source = new MetricSource(options);
            Assert.Throws<ArgumentException>("name", () => CreateTaggedMetric(source, StringTag));
        }

        [Fact]
        public virtual void GetMetadata_ReturnsData()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var metric = CreateMetric(source);
            var metadata = metric.GetMetadata();

            Assert.Collection(
                metadata,
                metadata =>
                {
                    Assert.Equal(metric.Name, metadata.Metric);
                    Assert.Equal(MetadataNames.Rate, metadata.Name);
                    switch (metric.MetricType)
                    {
                        case MetricType.Counter:
                        case MetricType.CumulativeCounter:
                            Assert.Equal("counter", metadata.Value);
                            break;
                        case MetricType.Gauge:
                            Assert.Equal("gauge", metadata.Value);
                            break;
                    }
                },
                metadata =>
                {
                    Assert.Equal(metric.Name, metadata.Metric);
                    Assert.Equal(MetadataNames.Description, metadata.Name);
                    Assert.Equal(metric.Description, metadata.Value);
                },
                metadata =>
                {
                    Assert.Equal(metric.Name, metadata.Metric);
                    Assert.Equal(MetadataNames.Unit, metadata.Name);
                    Assert.Equal(metric.Unit, metadata.Value);
                }
            );
        }

        [Fact]
        public void GetReadings_HasDefaultTags()
        {
            var options = new MetricSourceOptions
            {
                DefaultTags =
                {
                    ["host"] = Environment.MachineName
                }
            };

            var source = new MetricSource(options);
            var metric = CreateMetric(source);
            UpdateMetric(metric);
            var readings = metric.GetReadings(DateTime.UtcNow).ToList();
            Assert.NotEmpty(readings);
            var reading = readings[0];
            Assert.Collection(
                reading.Tags,
                tag =>
                {
                    var transformedKey = options.TagNameTransformer("host");
                    var transformedValue = options.TagValueTransformer(transformedKey, Environment.MachineName);

                    Assert.Equal(transformedKey, tag.Key);
                    Assert.Equal(transformedValue, tag.Value);
                });
        }

        [Fact]
        public virtual void GetReadings_Resets()
        {
            var options = new MetricSourceOptions();
            var source = new MetricSource(options);
            var c = CreateMetric(source);
            UpdateMetric(c);
            var readings = c.GetReadings(DateTime.UtcNow);
            Assert.NotEmpty(readings);
            readings = c.GetReadings(DateTime.UtcNow);
            Assert.Empty(readings);
        }

        protected abstract TMetric CreateMetric(MetricSource source);
        protected abstract void UpdateMetric(TMetric metric);
        protected abstract TTaggedMetric CreateTaggedMetric(MetricSource source, in MetricTag<string> tag);
        protected abstract TMultiTaggedMetric CreateTaggedMetric(MetricSource source, in MetricTag<string> tag1, in MetricTag<TagValues> tag2);
        protected abstract void UpdateMetric(TTaggedMetric metric, string tag1);
        protected abstract void UpdateMetric(TMultiTaggedMetric metric, string tag1, TagValues tag2);

        protected void AssertTagEqual<T>(MetricSourceOptions options, KeyValuePair<string, string> tag, in MetricTag<T> tagMetadata, T value)
        {
            var transformedKey = options.TagNameTransformer(tagMetadata.Name);
            var transformedValue = options.TagValueTransformer(transformedKey, value);

            Assert.Equal(transformedKey, tag.Key);
            Assert.Equal(transformedValue, tag.Value);
        }
    }

    public enum TagValues
    {
        A,
        B,
    }
}
