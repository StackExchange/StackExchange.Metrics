namespace StackExchange.Metrics.SampleHost
{
    public class PerfCounters
    {
        private readonly MetricGroup<string, MyCounterCategory, MyCounter> _myCounter;

        public PerfCounters(IMetricsCollector collector)
        {
            _myCounter = collector.GetMetricGroup<string, MyCounterCategory, MyCounter>("my_counter", "counts", "counts of my counting", (t, c) => new MyCounter(t, c));
        }

        public void IncrementMyCounter(string tag, MyCounterCategory category)
        {
            _myCounter.Add(tag, category).Increment();
        }
    }
}
