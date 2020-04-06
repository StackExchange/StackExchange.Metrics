using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using StackExchange.Metrics.Infrastructure;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    public class MetricTagAttributeTests
    {
        [Fact]
        public void ReadWritePropertyThrows()
        {
            var metric = new MetricWithTaggedReadWriteProperty();
            var defaultTags = ImmutableDictionary<string, string>.Empty;
            var tagConverter = new TagValueConverterDelegate((name, value) => value);

            Assert.Throws<InvalidOperationException>(
                () => metric.GetTags(defaultTags, tagConverter, x => x,new Dictionary<Type, List<MetricTag>>())
            );
        }

        [Fact]
        public void ReadWriteFieldThrows()
        {
            var metric = new MetricWithTaggedReadWriteField();
            var defaultTags = ImmutableDictionary<string, string>.Empty;
            var tagConverter = new TagValueConverterDelegate((name, value) => value);

            Assert.Throws<InvalidOperationException>(
                () => metric.GetTags(defaultTags, tagConverter, x => x,new Dictionary<Type, List<MetricTag>>())
            );
        }

        [Fact]
        public void ReadOnlyPropertyReturnsValue()
        {
            var metric = new MetricWithTaggedReadOnlyProperty("test");
            var defaultTags = ImmutableDictionary<string, string>.Empty;
            var tagConverter = new TagValueConverterDelegate((name, value) => value);
            var tags = metric.GetTags(defaultTags, tagConverter, x => x, new Dictionary<Type, List<MetricTag>>());

            Assert.True(tags.ContainsKey("Tag"));
            Assert.Equal("test", tags["Tag"]);
        }

        [Fact]
        public void ReadOnlyFieldReturnsValue()
        {
            var metric = new MetricWithTaggedReadOnlyField("test");
            var defaultTags = ImmutableDictionary<string, string>.Empty;
            var tagConverter = new TagValueConverterDelegate((name, value) => value);
            var tags = metric.GetTags(defaultTags, tagConverter, x => x, new Dictionary<Type, List<MetricTag>>());

            Assert.True(tags.ContainsKey("Tag"));
            Assert.Equal("test", tags["Tag"]);
        }

        [Fact]
        public void EnumMembersReturnValues()
        {
            var metric = new MetricWithTaggedEnumMembers(TagValue.One, TagValue.Two);
            var defaultTags = ImmutableDictionary<string, string>.Empty;
            var tagConverter = new TagValueConverterDelegate((name, value) => value);
            var tags = metric.GetTags(defaultTags, tagConverter, x => x, new Dictionary<Type, List<MetricTag>>());

            Assert.True(tags.ContainsKey("Field"));
            Assert.Equal("One", tags["Field"]);
            Assert.True(tags.ContainsKey("Property"));
            Assert.Equal("Two", tags["Property"]);
        }

        [Theory]
        [InlineData(typeof(int))]
        [InlineData(typeof(byte))]
        [InlineData(typeof(long))]
        [InlineData(typeof(short))]
        [InlineData(typeof(double))]
        [InlineData(typeof(float))]
        [InlineData(typeof(object))]
        [InlineData(typeof(CustomStruct))]
        [InlineData(typeof(CustomClass))]
        public void InvalidFieldTypesThrow(Type type)
        {
            var metricType = typeof(MetricWithInvalidField<>).MakeGenericType(type);
            var metric = (MetricBase)Activator.CreateInstance(metricType);
            var defaultTags = ImmutableDictionary<string, string>.Empty;
            var tagConverter = new TagValueConverterDelegate((name, value) => value);

            Assert.Throws<InvalidOperationException>(
                () => metric.GetTags(defaultTags, tagConverter, x => x,new Dictionary<Type, List<MetricTag>>())
            );
        }

        [Fact]
        public void CustomTagName()
        {
            var metric = new MetricWithCustomNaming("custom-name");
            var defaultTags = ImmutableDictionary<string, string>.Empty;
            var tagConverter = new TagValueConverterDelegate((name, value) => value);
            var tags = metric.GetTags(defaultTags, tagConverter, x => x, new Dictionary<Type, List<MetricTag>>());

            Assert.True(tags.ContainsKey("my-awesome-tag"));
            Assert.False(tags.ContainsKey("Tag"));
            Assert.Equal("custom-name", tags["my-awesome-tag"]);
        }

        [Fact]
        public void DefaultTagsAreOverridden()
        {
            var metric = new MetricWithTaggedReadOnlyProperty("value");
            var defaultTags = ImmutableDictionary<string, string>.Empty.Add("Tag", "default");
            var tagConverter = new TagValueConverterDelegate((name, value) => value);
            var tags = metric.GetTags(defaultTags, tagConverter, x => x, new Dictionary<Type, List<MetricTag>>());

            Assert.True(tags.ContainsKey("Tag"));
            Assert.Equal("value", tags["Tag"]);
        }

        [Fact]
        public void TagValuesAreConverted()
        {
            var metric = new MetricWithTaggedReadOnlyProperty("value");
            var defaultTags = ImmutableDictionary<string, string>.Empty;
            var tagConverter = new TagValueConverterDelegate((name, value) => value.ToUpper());
            var tags = metric.GetTags(defaultTags, tagConverter, x => x, new Dictionary<Type, List<MetricTag>>());

            Assert.True(tags.ContainsKey("Tag"));
            Assert.Equal("VALUE", tags["Tag"]);
        }

        private abstract class TestMetric : MetricBase
        {
            public override MetricType MetricType { get; } = MetricType.Counter;

            protected override void Serialize(IMetricBatch writer, DateTime now)
            {
            }
        }

        private class MetricWithTaggedReadWriteProperty : TestMetric
        {
            [MetricTag] public string Tag { get; set; }
        }

        private class MetricWithTaggedReadWriteField : TestMetric
        {
            [MetricTag] public string Tag;
        }

        private class MetricWithTaggedReadOnlyProperty : TestMetric
        {
            public MetricWithTaggedReadOnlyProperty(string tag) => (Tag) = tag;

            [MetricTag] public string Tag { get; }
        }

        private class MetricWithTaggedReadOnlyField : TestMetric
        {
            public MetricWithTaggedReadOnlyField(string tag) => (Tag) = tag;

            [MetricTag] public readonly string Tag;
        }

        private enum TagValue
        {
            One,
            Two,
        }

        private class MetricWithTaggedEnumMembers : TestMetric
        {
            public MetricWithTaggedEnumMembers(TagValue field, TagValue property) =>
                (Field, Property) = (field, property);

            [MetricTag] public readonly TagValue Field;
            [MetricTag] public TagValue Property { get; }
        }

        private class MetricWithInvalidField<T> : TestMetric
        {
            public MetricWithInvalidField()
            {
                Tag = default(T);
            }

            [MetricTag] public readonly T Tag;
        }

        private class CustomClass
        {
        }

        private struct CustomStruct
        {
        }

        private class MetricWithCustomNaming : TestMetric
        {
            public MetricWithCustomNaming(string tag) => (Tag) = (tag);
            [MetricTag(name: "my-awesome-tag")]
            public string Tag { get; }
        }
    }
}
