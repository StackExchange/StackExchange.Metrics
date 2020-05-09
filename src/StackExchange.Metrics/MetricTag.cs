namespace StackExchange.Metrics
{
    /// <summary>
    /// Represents the type and name of a tag that is added to a 
    /// </summary>
    /// <typeparam name="T">
    /// Type of the tag.
    /// </typeparam>
    public readonly struct MetricTag<T>
    {
        /// <summary>
        /// Constructs a new <see cref="MetricTag{T}"/> with the specified name.
        /// </summary>
        /// <param name="name">
        /// Name of the tag.
        /// </param>
        public MetricTag(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Gets the name of the tag.
        /// </summary>
        public string Name { get; }
    }
}
