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

            var i = 0;
            Func<Uri> getUrl = () =>
            {
//                i++;
//                if (i < 2)
//                    return null;
//                if (i < 6)
//                    return new Uri("http://192.168.59.105:8070/");

                return new Uri("http://192.168.59.104:8070/");
            };

            var options = new BosunReporterOptions()
            {
                MetricsNamePrefix = "bret.",
                GetBosunUrl = getUrl,
                ThrowOnPostFail = false,
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
