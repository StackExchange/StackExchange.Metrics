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
        /// The JSON-serialized tagset for the time series.
        /// </summary>
        public string Tags { get; }
        /// <summary>
        /// The value of the metadata. For example, if Name = "desc", then Value = "your description text here"
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Describes a piece of time series metadata which can be sent to Bosun.
        /// </summary>
        /// <param name="metric">The metric name. Make sure to use the fully-prefixed name.</param>
        /// <param name="name">The type of metadata being sent. Should be one of "rate", "desc", or "unit".</param>
        /// <param name="tags">The JSON-serialized tagset for the time series.</param>
        /// <param name="value">The value of the metadata. For example, if Name = "desc", then Value = "your description text here"</param>
        public MetaData(string metric, string name, string tags, string value)
        {
            Metric = metric;
            Name = name;
            Tags = tags;
            Value = value;
        }
    }
}
