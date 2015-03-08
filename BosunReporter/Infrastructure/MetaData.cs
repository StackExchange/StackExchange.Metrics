namespace BosunReporter.Infrastructure
{
    public class MetaData
    {
        public string Metric { get; }
        public string Name { get; }
        public string Value { get; }

        public MetaData(string metric, string name, string value)
        {
            Metric = metric;
            Name = name;
            Value = value;
        }
    }
}
