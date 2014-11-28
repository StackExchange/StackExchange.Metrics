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
                ReportingInterval = 30,
                PropertyToTagName = NameTransformers.CamelToLowerSnakeCase
            };
            var reporter = new BosunReporter.BosunReporter(options);
            var counter = reporter.GetMetric<TestCounter>("my_counter");
            counter.Increment();
            counter.Increment();

            var gauge = reporter.GetMetric("my_gauge", new TestAggregateGauge("1"));
            if (gauge != reporter.GetMetric("my_gauge", new TestAggregateGauge("1")))
                throw new Exception("WAT?");

            //reporter.GetMetric<TestAggregateGauge>("my_gauge_95"); // <- should throw an exception

            for (var i = 0; i < 6; i++)
            {
                new Thread(Run).Start(new Tuple<BosunAggregateGauge, BosunAggregateGauge, int>(gauge, reporter.GetMetric("my_gauge", new TestAggregateGauge("2")), i));
            }
        }

        static void Run(object obj)
        {
            var tup = (Tuple<BosunAggregateGauge, BosunAggregateGauge, int>)obj;
            var gauge1 = tup.Item1;
            var gauge2 = tup.Item2;
            var rand = new Random(tup.Item3);
            int i;
            while (true)
            {
                for (i = 0; i < 20; i++)
                {
                    gauge1.Record(rand.NextDouble());
                    gauge2.Record(rand.NextDouble());
                }
                Thread.Sleep(1);
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

        public TestAggregateGauge(string something)
        {
            Host = "bret-host";
            SomeTagName = something;
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
