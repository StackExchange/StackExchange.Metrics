using System.Collections.Generic;

namespace BosunReporter.Infrastructure
{
    /// <summary>
    /// Describes a piece of time series metadata which can be sent to Bosun.
    /// </summary>
    public class MetaData
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
        public IReadOnlyDictionary<string, string> Tags { get; }
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
        public MetaData(string metric, string name, IReadOnlyDictionary<string, string> tags, string value)
        {
            Metric = metric;
            Name = name;
            Tags = tags;
            Value = value;
        }
    }
}
