using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BosunReporter;
using BosunReporter.Metrics;

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
                return new Uri("http://192.168.99.100:8070/");
            };

            // for testing minimum event threshold
//            AggregateGauge.GetDefaultMinimumEvents = () => 1306000;

            var options = new BosunOptions()
            {
                MetricsNamePrefix = "bret.",
                GetBosunUrl = getUrl,
                ThrowOnPostFail = true,
                ReportingInterval = 5,
                PropertyToTagName = NameTransformers.CamelToLowerSnakeCase,
                TagValueConverter = (name, value) => name == "converted" ? value.ToLowerInvariant() : value,
                DefaultTags = new Dictionary<string, string> { {"host", NameTransformers.Sanitize(Environment.MachineName.ToLower())} }
            };
            var collector = new MetricsCollector(options);

            collector.OnBackgroundException += exception =>
            {
                Console.WriteLine("Hey, there was an exception.");
                Console.WriteLine(exception);
            };

            collector.BeforeSerialization += () => Debug.WriteLine("BosunReporter: Running metrics snapshot.");
            collector.AfterSerialization += info => Debug.WriteLine($"BosunReporter: Metric Snapshot took {info.MillisecondsDuration.ToString("0.##")}ms");
            collector.AfterPost += info => Debug.WriteLine($"BosunReporter: {info.Count} metrics posted to Bosun in {info.MillisecondsDuration.ToString("0.##")}ms ({(info.Successful ? "SUCCESS" : "FAILED")})");

            collector.BindMetric("my_counter", "increments", typeof(TestCounter));
            var counter = collector.GetMetric<TestCounter>("my_counter", "increments", "This is meaningless.");
            counter.Increment();
            counter.Increment();
            
            var gauge = collector.CreateMetric("gauge", "watts", "Some description of a gauge.", new TestAggregateGauge("1"));
            if (gauge != collector.GetMetric("gauge", "watts", null, new TestAggregateGauge("1")))
                throw new Exception("WAT?");

            try
            {
                collector.CreateMetric("gauge", "watts", "Some description of a gauge.", new TestAggregateGauge("1"));
            }
            catch(Exception)
            {
                goto SKIP_EXCEPTION;
            }

            throw new Exception("CreateMetric should have failed for duplicate metric.");

            SKIP_EXCEPTION:

            var gauge2 = collector.GetMetric<AggregateGauge>("gauge2", "newtons", "Number of newtons currently applied.");
            for (var i = 0; i < 6; i++)
            {
                new Thread(Run).Start(new Tuple<AggregateGauge, AggregateGauge, int>(gauge, gauge2, i));
            }

            var enumCounter = collector.GetMetricGroup<SomeEnum, EnumCounter>("some_enum", "things", "Some of something");
            enumCounter.PopulateFromEnum();

            Type t;
            string u;
            if (collector.TryGetMetricInfo("gauge2", out t, out u))
            {
                Console.WriteLine(t);
                Console.WriteLine(u);
            }
            else
            {
                Console.WriteLine("NOOOOO!!!!!");
            }

            var si = 0;
            var snapshot = collector.GetMetric("my_snapshot", "snappys", "Snap snap snap.", new SnapshotGauge(() => ++si % 5));

            var group = collector.GetMetricGroup<string, TestGroupGauge>("test_group", "tests", "These gauges are for testing.");
            group.Add("low").Description = "Low testing.";
            group.Add("medium").Description = "Medium testing.";
            group.Add("high").Description = "High testing.";
            var sampler = collector.GetMetric("sampler", "french fries", "Collect them all.", new SamplingGauge());
            var eventGauge = collector.GetMetric("event", "count", "How many last time.", new EventGauge());
            var converted = collector.CreateMetric("convert_test", "units", "Checking to see if the tag value converter works.", new ConvertedTagsTestCounter("ThingsAndStuff"));

            var sai = 0;
            var random = new Random();
            _samplerTimer = new Timer(o => 
                {
                    sampler.Record(++sai%35);
                    eventGauge.Record(sai%35);
                    group["low"].Record(random.Next(0, 10));
                    group["medium"].Record(random.Next(10, 20));
                    group["high"].Record(random.Next(20, 30));

                    enumCounter[SomeEnum.One].Increment();
                    enumCounter[SomeEnum.Two].Increment(2);
                    enumCounter[SomeEnum.Three].Increment(3);
                    enumCounter[SomeEnum.Four].Increment(4);

                    converted.Increment();

                    if (sai == 40)
                    {
                        collector.Shutdown();
                        Environment.Exit(0);
                    }

                }, null, 1000, 1000);

            Thread.Sleep(4000);
            collector.UpdateDefaultTags(new Dictionary<string, string> { { "host", NameTransformers.Sanitize(Environment.MachineName.ToLower()) } });
//            Thread.Sleep(4000);
//            collector.UpdateDefaultTags(new Dictionary<string, string>() { { "host", "test_env" } });
        }

        static void Run(object obj)
        {
            var tup = (Tuple<AggregateGauge, AggregateGauge, int>)obj;
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

    [GaugeAggregator(AggregateMode.Count)]
    [GaugeAggregator(AggregateMode.Average)]
    [GaugeAggregator(AggregateMode.Max)]
    [GaugeAggregator(AggregateMode.Min)]
    [GaugeAggregator(AggregateMode.Median)]
    [GaugeAggregator(AggregateMode.Percentile, 0.95)]
    [GaugeAggregator(AggregateMode.Percentile, 0.25)]
    public class TestAggregateGauge : AggregateGauge
    {
//        [BosunTag] public readonly string Host;
        [BosunTag] public readonly string SomeTagName;

        public TestAggregateGauge(string something)
        {
//            Host = "bret-host";
            SomeTagName = something;
        }
    }

    public class TestCounter : Counter
    {
//        [BosunTag] public readonly string Host;

        public TestCounter()
        {
//            Host = "bret-host";
        }
    }

    [IgnoreDefaultBosunTags]
    public class TestSnapshotGauge : SnapshotGauge
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

    public class TestGroupGauge : EventGauge
    {
        [BosunTag]
        public readonly string Range;

        public TestGroupGauge(string range)
        {
            Range = range;
        }
    }

    public enum SomeEnum
    {
        One = 1,
        Two = 2,
        Three = 3,
        Four = 4,
    }

    public class EnumCounter : Counter
    {
        [BosunTag] public readonly SomeEnum Value;

        public EnumCounter(SomeEnum val)
        {
            Value = val;
        }
    }

    public class ConvertedTagsTestCounter : Counter
    {
        [BosunTag] public readonly string Converted;

        public ConvertedTagsTestCounter(string toConvert)
        {
            Converted = toConvert;
        }
    }
}
