using System;
using System.Diagnostics;
using System.Threading;
using BosunReporter;

namespace Scratch
{
    class Program
    {
        static void Main(string[] args)
        {
            Debug.Listeners.Add(new TextWriterTraceListener(Console.Out));
            Debug.AutoFlush = true;

            var options = new BosunReporterOptions()
            {
                MetricsNamePrefix = "bret.",
                BosunUrl = new Uri("http://192.168.59.104:8070/"),
                ThrowOnPostFail = true,
                ReportingInterval = 5
            };
            var reporter = new BosunReporter.BosunReporter(options);
            var counter = reporter.GetMetric<TestCounter>("my_counter");
            counter.Increment();
            counter.Increment();

            var gauge = reporter.GetMetric<TestAggregateGauge>("my_gauge");
            if (gauge != reporter.GetMetric<TestAggregateGauge>("my_gauge"))
                throw new Exception("WAT?");

            //reporter.GetMetric<TestAggregateGauge>("my_gauge_95"); // <- should throw an exception

            var rand = new Random();
            while (true)
            {
                gauge.Record(rand.NextDouble());
                Thread.Sleep(5);
            }
        }
    }

    [GaugeAggregator(AggregateMode.Average)]
    [GaugeAggregator(AggregateMode.Max)]
    [GaugeAggregator(AggregateMode.Min)]
    [GaugeAggregator(AggregateMode.Median)]
    [GaugeAggregator(AggregateMode.Percentile, 0.95)]
    [GaugeAggregator(AggregateMode.Percentile, 0.25)]
    public class TestAggregateGauge : BosunAggregateGauge
    {
        [BosunTag("host")]
        public readonly string Host;

        public TestAggregateGauge()
        {
            Host = "bret-host";
        }
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
