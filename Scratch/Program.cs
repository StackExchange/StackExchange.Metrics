using System;
using BosunReporter;

namespace Scratch
{
    class Program
    {
        static void Main(string[] args)
        {
            var options = new BosunReporterOptions()
            {
                MetricsNamePrefix = "bret.",
                BosunUrl = new Uri("http://192.168.59.104:8070/"),
                ThrowOnPostFail = true
            };
            var reporter = new BosunReporter.BosunReporter(options);
            var counter = reporter.GetMetric<TestCounter>("my_counter");
            counter.Increment();
            counter.Increment();

            var gauge = reporter.GetMetric<TestGauge>("my_gauge");
            if (gauge != reporter.GetMetric<TestGauge>("my_gauge"))
                throw new Exception("WAT?");

            var rand = new Random();
            for (var i = 0; i < 10000; i++)
            {
                gauge.Record(rand.NextDouble());
            }

            reporter.SnapshotAndFlush();
        }
    }

    [GaugeAggregator(AggregateMode.Average)]
    [GaugeAggregator(AggregateMode.Max)]
    [GaugeAggregator(AggregateMode.Min)]
    [GaugeAggregator(AggregateMode.Median)]
    [GaugeAggregator(AggregateMode.Percentile, 0.95)]
    [GaugeAggregator(AggregateMode.Percentile, 0.25)]
    public class TestGauge : BosunGauge
    {
        //
    }

    public class TestCounter : BosunCounter
    {
        [BosunTag("host")]
        public readonly string Host;

        public TestCounter()
        {
            Host = "bret-host";
        }
    }
}
