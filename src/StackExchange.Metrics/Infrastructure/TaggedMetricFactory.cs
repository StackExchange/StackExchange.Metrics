using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Abstract base class used by derived implementations to generated tagged metrics.
    /// </summary>
    /// <typeparam name="TMetric">
    /// Type of metric that this factory can create.
    /// </typeparam>
    public abstract class TaggedMetricFactory<TMetric> : IMetricReadingWriter, IMetricMetadataProvider where TMetric : MetricBase
    {
        /// <summary>
        /// Used by derived classes to pass the name, description and unit for a tag.
        /// </summary>
        protected TaggedMetricFactory(string name, string description, string unit, MetricSourceOptions options)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            name = options.MetricNameTransformer(name);
            if (!options.MetricNameValidator(name))
            {
                throw new ArgumentException(name + " is not a valid metric name.", nameof(name));
            }

            Name = name;
            Description = description;
            Unit = unit;
            Options = options;
        }

        /// <summary>
        /// The metric name, excluding any suffixes
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Description of this metric (time series) which will be sent to metric handlers as metadata.
        /// </summary>
        public string Description { get; }
        /// <summary>
        /// The units for this metric (time series) which will be sent to metric handlers as metadata. (example: "milliseconds")
        /// </summary>
        public string Unit { get; }
        /// <summary>
        /// Gets the <see cref="MetricSourceOptions" /> used to provide default tags, 
        /// metric / tag name transformation, value transformation and validation.
        /// </summary>
        protected MetricSourceOptions Options { get; }

        /// <inheritdoc/>
        void IMetricReadingWriter.Write(IMetricReadingBatch batch, DateTime timestamp)
        {
            foreach (IMetricReadingWriter metric in GetMetrics())
            {
                metric.Write(batch, timestamp);
            }
        }

        /// <inheritdoc />
        public IEnumerable<Metadata> GetMetadata()
        {
            foreach (var metric in GetMetrics())
            {
                foreach (var metadata in metric.GetMetadata())
                {
                    yield return metadata;
                }
            }
        }

        /// <summary>
        /// In derived implementations, returns metrics created by the factory.
        /// </summary>
        protected abstract IEnumerable<TMetric> GetMetrics();

        /// <summary>
        /// In derived implementations, constructs an instance of <typeparamref name="TMetric"/>
        /// </summary>
        protected abstract TMetric Create(ImmutableDictionary<string, string> tags);

        /// <summary>
        /// Transforms and validates a <see cref="MetricTag{T}"/>.
        /// </summary>
        /// <typeparam name="T">Type of tag.</typeparam>
        /// <param name="tag"><see cref="MetricTag{T}"/> instance.</param>
        protected MetricTag<T> TransformAndValidate<T>(in MetricTag<T> tag)
        {
            var transformedName = Options.TagNameTransformer(tag.Name);
            if (!Options.TagNameValidator(transformedName))
            {
                throw new ArgumentException($"Invalid tag name specified. Name = {tag.Name}, Transformed = {transformedName}", nameof(tag));
            }

            if (transformedName != tag.Name)
            {
                return new MetricTag<T>(transformedName);
            }

            return tag;
        }

        /// <summary>
        /// Transforms and validates a tag value.
        /// </summary>
        /// <typeparam name="T">Type of tag.</typeparam>
        /// <param name="name">Name of the tag.</param>
        /// <param name="value">Value of the tag.</param>
        protected string TransformAndValidate<T>(string name, T value)
        {
            var transformedValue = Options.TagValueTransformer(name, value);
            if (!Options.TagValueValidator(transformedValue))
            {
                throw new ArgumentException($"Invalid tag value specified. Name = {name}, Value = {value}, Transformed = {transformedValue}", nameof(value));
            }

            return transformedValue;
        }

        /// <summary>
        /// Dictionary that exposes tagged metrics keyed by tag values
        /// in a thread-safe way.
        /// </summary>
        protected class TaggedMetricDictionary<TKey> : ConcurrentDictionary<TKey, TMetric>
        {
#if !NETCOREAPP
            /// <summary>
            /// Adds a key/value pair to the dictionary by using the specified
            /// function and an argument if the key does not exist, or returns the
            /// existing value if the key exists.
            /// </summary>
            public TMetric GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TMetric> valueFactory, TArg arg)
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                if (valueFactory == null) throw new ArgumentNullException(nameof(valueFactory));

                while (true)
                {
                    TMetric value;
                    if (TryGetValue(key, out value))
                        return value;

                    value = valueFactory(key, arg);
                    if (TryAdd(key, value))
                        return value;
                }
            }
#endif
        }
    }

}
