using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using StackExchange.Metrics.Metrics;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// The base class for all metrics (time series). Custom metric types may inherit from this directly. However, most users will want to use the factory methods
    /// on classes, such as <see cref="Counter"/> or <see cref="AggregateGauge"/>.
    /// </summary>
    public abstract class MetricBase : IMetricReadingWriter, IMetricMetadataProvider
    {
        /// <summary>
        /// <see cref="MetricType" /> value indicating the type of metric.
        /// </summary>
        public abstract MetricType MetricType { get; }

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
        /// Tags for this metric (time series).
        /// </summary>
        public ImmutableDictionary<string, string> Tags { get; }

        private ImmutableArray<SuffixMetadata> _suffixes;

        /// <summary>
        /// Metadata about suffixes associated with this metric. The only built-in metric type which has multiple suffixes
        /// is <see cref="AggregateGauge"/> where the suffixes will be things like "_avg", "_min", "_95", etc.
        /// </summary>
        protected ImmutableArray<SuffixMetadata> Suffixes => _suffixes.IsDefault ? (_suffixes = GetSuffixMetadata().ToImmutableArray()) : _suffixes;

        /// <summary>
        /// Instantiates the base class.
        /// </summary>
        protected MetricBase(string name, string unit, string description, MetricSourceOptions options, ImmutableDictionary<string, string> tags = null)
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

            var tagBuilder = options.DefaultTagsFrozen.ToBuilder();
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    tagBuilder[tag.Key] = tag.Value;
                }
            }

            Name = name;
            Unit = unit;
            Description = description;
            Tags = tagBuilder.ToImmutable();
        }

        /// <summary>
        /// Returns an <see cref="IEnumerable{T}"/> of suffix metadata for this metric. By default this contains
        /// metadata for just the base metric (i.e. no suffix). The only built-in metric type which has more than one 
        /// is <see cref="AggregateGauge"/> where the suffixes will be things like "_avg", "_min", "_95", etc.
        /// </summary>
        protected virtual IEnumerable<SuffixMetadata> GetSuffixMetadata()
        {
            yield return new SuffixMetadata(Name, Unit, Description);
        }

        /// <summary>
        /// Returns an enumerable of <see cref="Metadata"/> which describes this metric.
        /// </summary>
        public virtual IEnumerable<Metadata> GetMetadata()
        {
            for (var i = 0; i < Suffixes.Length; i++)
            {
                string metricType;
                switch (MetricType)
                {
                    case MetricType.Counter:
                    case MetricType.CumulativeCounter:
                        metricType = "counter";
                        break;
                    case MetricType.Gauge:
                        metricType = "gauge";
                        break;
                    default:
                        metricType = MetricType.ToString().ToLower();
                        break;

                }

                var suffix = Suffixes[i];

                yield return new Metadata(suffix.Name, MetadataNames.Rate, Tags, metricType);

                var desc = suffix.Description;
                if (!string.IsNullOrEmpty(desc))
                    yield return new Metadata(suffix.Name, MetadataNames.Description, Tags, desc);

                var unit = suffix.Unit;
                if (!string.IsNullOrEmpty(unit))
                    yield return new Metadata(suffix.Name, MetadataNames.Unit, Tags, unit);
            }
        }

        /// <inheritdoc/>
        void IMetricReadingWriter.WriteReadings(IMetricReadingBatch batch, DateTime timestamp) => WriteReadings(batch, timestamp);

        /// <summary>
        /// Writes the readings for a metric into the specified <see cref="IMetricReadingBatch"/>.
        /// </summary>
        /// <param name="batch">
        /// <see cref="IMetricReadingBatch"/> to write metrics into.
        /// </param>
        /// <param name="timestamp">
        /// Timestamp applied to all readings.
        /// </param>
        protected abstract void WriteReadings(IMetricReadingBatch batch, DateTime timestamp);

        /// <summary>
        /// Creates a <see cref="MetricReading"/> with the specified value and timestamp and all other
        /// properties inherited from this metric.
        /// </summary>
        protected MetricReading CreateReading(double value, DateTime timestamp) =>
            new MetricReading(
                name: Name,
                type: MetricType,
                value: value,
                tags: Tags,
                timestamp: timestamp
            );

        /// <summary>
        /// Creates a <see cref="MetricReading"/> with the specified value and timestamp and all other
        /// properties inherited from the specified suffix.
        /// </summary>
        protected MetricReading CreateReading(in SuffixMetadata suffix, double value, DateTime timestamp) =>
            new MetricReading(
                name: suffix.Name,
                type: MetricType,
                value: value,
                tags: Tags,
                timestamp: timestamp
            );

        /// <summary>
        /// Metadata about a suffix.
        /// </summary>
        protected readonly struct SuffixMetadata
        {
            /// <summary>
            /// Instantiates a new instance of <see cref="SuffixMetadata"/>
            /// </summary>
            public SuffixMetadata(string name, string unit, string description)
                => (Name, Unit, Description) = (name, unit, description);

            /// <summary>
            /// Gets the fully-qualified name of the metric including the suffix.
            /// </summary>
            public string Name { get; }
            /// <summary>
            /// Gets the unit of the suffix.
            /// </summary>
            public string Unit { get; }
            /// <summary>
            /// Gets the description of the suffix.
            /// </summary>
            public string Description { get; }
        }
    }
}
