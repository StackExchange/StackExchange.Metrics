using System.Collections.Immutable;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Describes a piece of time series metadata which can be sent to a metric handler.
    /// </summary>
    public class Metadata
    {
        /// <summary>
        /// The metric name.
        /// </summary>
        public string Metric { get; }
        /// <summary>
        /// The type of metadata being sent. Should be one of "rate", "desc", or "unit".
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Tags associated with a metric.
        /// </summary>
        public ImmutableDictionary<string, string> Tags { get; }
        /// <summary>
        /// The value of the metadata. For example, if Name = "desc", then Value = "your description text here"
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Describes a piece of time series metadata which can be sent to Bosun.
        /// </summary>
        /// <param name="metric">The metric name. Make sure to use the fully-prefixed name.</param>
        /// <param name="name">The type of metadata being sent. Should be one of "rate", "desc", or "unit".</param>
        /// <param name="tags">Dictionary of tags keyed by name.</param>
        /// <param name="value">The value of the metadata. For example, if Name = "desc", then Value = "your description text here"</param>
        public Metadata(string metric, string name, ImmutableDictionary<string, string> tags, string value)
        {
            Metric = metric;
            Name = name;
            Tags = tags;
            Value = value;
        }
    }
}
