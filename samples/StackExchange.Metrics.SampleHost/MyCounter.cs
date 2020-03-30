using StackExchange.Metrics.Metrics;

namespace StackExchange.Metrics.SampleHost
{
    public class MyCounter : Counter
    {
        [MetricTag] public readonly string tag;
        [MetricTag] public readonly MyCounterCategory category;

        public MyCounter(string tag, MyCounterCategory category)
        {
            this.tag = tag;
            this.category = category;
        }
    }

    public enum MyCounterCategory
    {
        Example_One,
        Example_Two,
    }
}
