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
                ReportingInterval = 5,
                PropertyToTagName = NameTransformers.CamelToLowerSnakeCase,
                DefaultTags = new Dictionary<string, string> { {"host", NameTransformers.Sanitize(Environment.MachineName.ToLower())} }
            };
            var collector = new MetricsCollector(options);

            collector.OnBackgroundException += exception =>
            {
                Console.WriteLine("Hey, there was an exception.");
                Console.WriteLine(exception);
            };

            collector.BindMetric("my_counter", typeof(TestCounter));
            var counter = collector.GetMetric<TestCounter>("my_counter");
            counter.Increment();
            counter.Increment();

            var gauge = collector.GetMetric("gauge", new TestAggregateGauge("1"));
            if (gauge != collector.GetMetric("gauge", new TestAggregateGauge("1")))
                throw new Exception("WAT?");

            gauge.Description = "This is some gauge.";
            gauge.Unit = "bytes";

            var gauge2 = collector.GetMetric<BosunAggregateGauge>("gauge2");
            for (var i = 0; i < 6; i++)
            {
                new Thread(Run).Start(new Tuple<BosunAggregateGauge, BosunAggregateGauge, int>(gauge, gauge2, i));
            }

            var si = 0;
            var snapshot = collector.GetMetric("my_snapshot", new BosunSnapshotGauge(() => ++si % 5));

            var group = new MetricGroup<string, TestGroupGauge>(collector, "test_group");
            group.Add("low");
            group.Add("medium");
            group.Add("high");
            var sampler = collector.GetMetric("sampler", new BosunSamplingGauge());
            var eventGauge = collector.GetMetric("event", new BosunEventGauge());
            var sai = 0;
            var random = new Random();
            _samplerTimer = new Timer(o => 
                {
                    sampler.Record(++sai%35);
                    eventGauge.Record(sai%35);
                    group["low"].Record(random.Next(0, 10));
                    group["medium"].Record(random.Next(10, 20));
                    group["high"].Record(random.Next(20, 30));

                    if (sai == 40)
                    {
                        collector.Shutdown();
                        Environment.Exit(0);
                    }

                }, null, 1000, 1000);

            Thread.Sleep(TimeSpan.FromSeconds(16));
            Console.WriteLine("removing...");
            Console.WriteLine(collector.RemoveMetric(counter));
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

    public class TestGroupGauge : BosunEventGauge
    {
        [BosunTag]
        public readonly string Range;

        public TestGroupGauge(string range)
        {
            Range = range;
        }
    }
}
