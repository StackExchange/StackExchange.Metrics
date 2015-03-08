namespace BosunReporter.Infrastructure
{
    public class MetaData
    {
        public string Metric { get; }
        public string Name { get; }
        public string Tags { get; }
        public string Value { get; }

        public MetaData(string metric, string name, string tags, string value)
        {
            Metric = metric;
            Name = name;
            Tags = tags;
            Value = value;
        }
    }
}
