using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Options used to customise how metrics are created.
    /// </summary>
    public class MetricSourceOptions : IOptions<MetricSourceOptions>
    {
        private static readonly NameTransformerDelegate s_defaultMetricNameTransformer = NameTransformers.Combine(NameTransformers.CamelToLowerSnakeCase, NameTransformers.Sanitize);
        private static readonly NameTransformerDelegate s_defaultTagNameTransformer = NameTransformers.Combine(NameTransformers.CamelToLowerSnakeCase, NameTransformers.Sanitize);
        private static readonly TagValueTransformerDelegate s_defaultTagValueTransformer = (_, value) => value.ToString().ToLowerInvariant();
        private static readonly ValidationDelegate s_defaultMetricNameValidator = MetricValidation.IsValidMetricName;
        private static readonly ValidationDelegate s_defaultTagNameValidator = MetricValidation.IsValidTagName;
        private static readonly ValidationDelegate s_defaultTagValueValidator = MetricValidation.IsValidTagValue;

        private NameTransformerDelegate _metricNameTransformer;
        private NameTransformerDelegate _tagNameTransformer;
        private TagValueTransformerDelegate _tagValueTransformer;
        private ValidationDelegate _metricNameValidator;
        private ValidationDelegate _tagNameValidator;
        private ValidationDelegate _tagValueValidator;

        /// <summary>
        /// Initializes a new instance of <see cref="MetricSourceOptions"/>.
        /// </summary>
        public MetricSourceOptions()
        {
            MetricNameTransformer = s_defaultMetricNameTransformer;
            TagNameTransformer = s_defaultTagNameTransformer;
            TagValueTransformer = s_defaultTagValueTransformer;
            MetricNameValidator = s_defaultMetricNameValidator;
            TagNameValidator = s_defaultTagNameValidator;
            TagValueValidator = s_defaultTagValueValidator;
            DefaultTags = new TagDictionary(this);
        }

        /// <summary>
        /// Gets or sets the function which takes a metric and returns a possibly altered value. This could be used as a global sanitizer
        /// or normalizer. It is applied to all metric names. If the return value is not a valid metric name
        /// an exception will be thrown.
        /// </summary>
        public NameTransformerDelegate MetricNameTransformer
        {
            get => _metricNameTransformer;
            set => _metricNameTransformer = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets or sets the function which takes a tag name and returns a possibly altered value. This could be used as a global sanitizer
        /// or normalizer. It is applied to all tag names, including default tags. If the return value is not a valid tag name
        /// an exception will be thrown.
        /// </summary>
        public NameTransformerDelegate TagNameTransformer
        {
            get => _tagNameTransformer;
            set => _tagNameTransformer = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets or sets the function which takes a tag name and value, and returns a possibly altered value. This could be used as a global sanitizer
        /// or normalizer. It is applied to all tag values, including default tags. If the return value is not a valid tag, an exception will be
        /// thrown. Null values are possible for the tagValue argument, so be sure to handle nulls appropriately.
        /// </summary>
        public TagValueTransformerDelegate TagValueTransformer
        {
            get => _tagValueTransformer;
            set => _tagValueTransformer = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets or sets the function that which returns whether a metric name is valid or not.
        /// </summary>
        public ValidationDelegate MetricNameValidator
        {
            get => _metricNameValidator;
            set => _metricNameValidator = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets or sets the function that which returns whether a tag name is valid or not.
        /// </summary>
        public ValidationDelegate TagNameValidator
        {
            get => _tagNameValidator;
            set => _tagNameValidator = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets or sets the function that which returns whether a tag value is valid or not.
        /// </summary>
        public ValidationDelegate TagValueValidator
        {
            get => _tagValueValidator;
            set => _tagValueValidator = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets tag name/value pairs that are applied to all metrics.
        /// </summary>
        public IDictionary<string, string> DefaultTags { get; }

        /// <summary>
        /// Gets an immutable version of <see cref="DefaultTagsFrozen"/>.
        /// </summary>
        internal ImmutableDictionary<string, string> DefaultTagsFrozen { get; private set; } = ImmutableDictionary<string, string>.Empty;

        private class TagDictionary : Dictionary<string, string>, IDictionary<string, string>
        {
            private readonly MetricSourceOptions _options;

            public TagDictionary(MetricSourceOptions options)
            {
                _options = options;
            }

            void ICollection<KeyValuePair<string, string>>.Clear()
            {
                Clear();
                SyncFrozen();
            }

            string IDictionary<string, string>.this[string key]
            {
                get => this[key];
                set
                {
                    var transformedName = _options.TagNameTransformer(key);
                    var transformedValue = _options.TagValueTransformer(key, value);
                    this[transformedName] = transformedValue;
                    SyncFrozen();
                }
            }

            bool IDictionary<string, string>.Remove(string key)
            {
                var removed = Remove(key);
                if (removed)
                {
                    SyncFrozen();
                }
                return removed;
            }

            void IDictionary<string, string>.Add(string key, string value)
            {
                var transformedName = _options.TagNameTransformer(key);
                var transformedValue = _options.TagValueTransformer(key, value);

                if (!_options.TagNameValidator(transformedName))
                {
                    throw new ArgumentException($"Invalid tag name specified. Name = {key}, Transformed = {transformedName}", nameof(key));
                }

                if (!_options.TagValueValidator(transformedValue))
                {
                    throw new ArgumentException($"Invalid tag value specified. Name = {key}, Value = {value}. Transformed = {transformedValue}", nameof(value));
                }

                Add(transformedName, transformedValue);
                SyncFrozen();
            }

            private void SyncFrozen() => _options.DefaultTagsFrozen = this.ToImmutableDictionary();
        }

        /// <summary>
        /// For easy usage without <see cref="Options.Create{TOptions}(TOptions)"/>.
        /// </summary>
        MetricSourceOptions IOptions<MetricSourceOptions>.Value => this;
    }
}
