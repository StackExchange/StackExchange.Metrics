using System;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    public class MetricSourceOptionsTests
    {
        [Fact]
        public void DefaultTags_FailedTagNameValidation()
        {
            var options = new MetricSourceOptions
            {
                TagNameValidator = _ => false
            };

            Assert.Throws<ArgumentException>("key", () => options.DefaultTags.Add("name", "value"));
        }

        [Fact]
        public void DefaultTags_FailedTagValueValidation()
        {
            var options = new MetricSourceOptions
            {
                TagValueValidator = _ => false
            };

            Assert.Throws<ArgumentException>("value", () => options.DefaultTags.Add("name", "value"));
        }

        [Fact]
        public void DefaultTags_TagNamesAreTransformed()
        {
            var options = new MetricSourceOptions
            {
                TagNameTransformer = v => v.ToUpper()
            };

            options.DefaultTags.Add("name", "value");

            Assert.True(options.DefaultTags.ContainsKey("NAME"));
        }

        [Fact]
        public void DefaultTags_TagValuesAreTransformed()
        {
            var options = new MetricSourceOptions
            {
                TagValueTransformer = (n, v) => v.ToString().ToUpper()
            };

            options.DefaultTags.Add("name", "value");

            Assert.True(options.DefaultTags.TryGetValue("name", out var value) && value == "VALUE");
        }

        [Fact]
        public void DefaultTags_AreSynchronizedWithFrozenWhenAdded()
        {
            var options = new MetricSourceOptions();
            options.DefaultTags.Add("name", "value");
            Assert.True(options.DefaultTagsFrozen.TryGetValue("name", out var value) && value == "value");
        }

        [Fact]
        public void DefaultTags_AreSynchronizedWithFrozenWhenRemoved()
        {
            var options = new MetricSourceOptions();
            options.DefaultTags.Add("name", "value");
            Assert.True(options.DefaultTagsFrozen.TryGetValue("name", out var value) && value == "value");
            options.DefaultTags.Remove("name");
            Assert.False(options.DefaultTagsFrozen.ContainsKey("name"));
        }

        [Fact]
        public void DefaultTags_AreSynchronizedWithFrozenWhenSet()
        {
            var options = new MetricSourceOptions();
            options.DefaultTags.Add("name", "value");
            Assert.True(options.DefaultTagsFrozen.TryGetValue("name", out var value) && value == "value");
            options.DefaultTags["name"] = "value_1";
            Assert.True(options.DefaultTagsFrozen.TryGetValue("name", out value) && value == "value_1");
        }

        [Fact]
        public void DefaultTags_AreSynchronizedWithFrozenWhenCleared()
        {
            var options = new MetricSourceOptions();
            options.DefaultTags.Add("name", "value");
            Assert.True(options.DefaultTagsFrozen.TryGetValue("name", out var value) && value == "value");
            options.DefaultTags.Clear();
            Assert.True(options.DefaultTagsFrozen.Count == 0);
        }
    }
}
