using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BosunReporter;

namespace Scratch
{
    class Program
    {
        private static Timer _samplerTimer;

        static void Main(string[] args)
        {
            Debug.Listeners.Add(new TextWriterTraceListener(Console.Out));
            Debug.AutoFlush = true;

            Func<Uri> getUrl = () =>
            {
                return new Uri("http://192.168.59.103:8070/");
            };

            var options = new BosunOptions()
            {
                MetricsNamePrefix = "bret.",
                GetBosunUrl = getUrl,
                ThrowOnPostFail = true,
                ReportingInterval = 30,
                PropertyToTagName = NameTransformers.CamelToLowerSnakeCase,
                DefaultTags = new Dictionary<string, string> { {"host", NameTransformers.Sanitize(Environment.MachineName.ToLower())} }
            };
            var reporter = new MetricsCollector(options);

            reporter.BindMetric("my_counter", typeof(TestCounter));
            var counter = reporter.GetMetric<TestCounter>("my_counter");
            counter.Increment();
            counter.Increment();

            var gauge = reporter.GetMetric("gauge", new TestAggregateGauge("1"));
            if (gauge != reporter.GetMetric("gauge", new TestAggregateGauge("1")))
                throw new Exception("WAT?");

            //reporter.GetMetric<TestAggregateGauge>("my_gauge_95"); // <- should throw an exception

            var gauge2 = reporter.GetMetric<BosunAggregateGauge>("gauge2");
            for (var i = 0; i < 6; i++)
            {
                new Thread(Run).Start(new Tuple<BosunAggregateGauge, BosunAggregateGauge, int>(gauge, gauge2, i));
            }

            var si = 0;
            var snapshot = reporter.GetMetric("my_snapshot", new BosunSnapshotGauge(() => ++si % 5));

            var sampler = reporter.GetMetric("sampler", new BosunSamplingGauge());
            var eventGauge = reporter.GetMetric("event", new BosunEventGauge());
            var sai = 0;
            _samplerTimer = new Timer(o => { sampler.Record(++sai%35); eventGauge.Record(sai%35); }, null, 1000, 1000);

            Thread.Sleep(TimeSpan.FromSeconds(16));
            Console.WriteLine("removing...");
            Console.WriteLine(reporter.RemoveMetric(counter));
        }

        static void Run(object obj)
        {
            var tup = (Tuple<BosunAggregateGauge, BosunAggregateGauge, int>)obj;
            var gauge1 = tup.Item1;
            var gauge2 = tup.Item2;

            if (gauge1 == gauge2)
                throw new Exception("These weren't supposed to be the same... they have different tags.");

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
//        [BosunTag] public readonly string Host;
        [BosunTag] public readonly string SomeTagName;

        public TestAggregateGauge(string something)
        {
//            Host = "bret-host";
            SomeTagName = something;
        }
    }

    public class TestCounter : BosunCounter
    {
//        [BosunTag] public readonly string Host;

        public TestCounter()
        {
//            Host = "bret-host";
        }
    }

    [IgnoreDefaultBosunTags]
    public class TestSnapshotGauge : BosunSnapshotGauge
    {
        [BosunTag] public readonly string Thing;

        public Func<double> GetValueLambda;

        public TestSnapshotGauge(Func<double> getValue)
        {
            Thing = "nothing";
            GetValueLambda = getValue;
        }

        protected override double? GetValue()
        {
            return GetValueLambda();
        }
    }
}
