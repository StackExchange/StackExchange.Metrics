using System;
using System.Diagnostics;
using System.Linq;
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

            Func<Uri> getUrl = () =>
            {
                return new Uri("http://192.168.59.104:8070/");
            };

            var options = new BosunReporterOptions()
            {
                MetricsNamePrefix = "bret.",
                GetBosunUrl = getUrl,
                ThrowOnPostFail = true,
                ReportingInterval = 5,
                PropertyToTagName = NameTransformers.CamelToLowerSnakeCase
            };
            var reporter = new BosunReporter.BosunReporter(options);
            var counter = reporter.GetMetric<TestCounter>("my_counter");
            counter.Increment();
            counter.Increment();

            var gauge = reporter.GetMetric<TestAggregateGauge>("my_gauge");
            if (gauge != reporter.GetMetric<TestAggregateGauge>("my_gauge"))
                throw new Exception("WAT?");

            //reporter.GetMetric<TestAggregateGauge>("my_gauge_95"); // <- should throw an exception

            for (var i = 0; i < 6; i++)
            {
                new Thread(Run).Start(new Tuple<BosunAggregateGauge, int>(gauge, i));
            }
        }

        static void Run(object obj)
        {
            var tup = (Tuple<BosunAggregateGauge, int>) obj;
            var gauge = tup.Item1;
            var rand = new Random(tup.Item2);
            var sleep = rand.Next(2, 8);
            while (true)
            {
                gauge.Record(rand.NextDouble());
                Thread.Sleep(sleep);
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
        [BosunTag] public readonly string Host;
        [BosunTag] public readonly string SomeTagName;

        public TestAggregateGauge()
        {
            Host = "bret-host";
            SomeTagName = "Something";
        }
    }

    public class TestCounter : BosunCounter
    {
        [BosunTag]
        public readonly string Host;

        public TestCounter()
        {
            Host = "bret-host";
        }
    }
}
