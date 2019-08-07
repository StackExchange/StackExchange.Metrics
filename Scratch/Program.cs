﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BosunReporter;
using BosunReporter.Handlers;
using BosunReporter.Infrastructure;
using BosunReporter.Metrics;

namespace Scratch
{
    class Program
    {
        static Task s_samplerTask;
        static CancellationTokenSource s_cancellationTokenSource;

        static async Task Main(string[] args)
        {
            Debug.AutoFlush = true;

            const string LocalEndpointKey = "Local";

            var localHandler = new LocalMetricHandler();
            var options = new BosunOptions(exception =>
            {
                Console.WriteLine("Hey, there was an exception.");
                Console.WriteLine(exception);
            })
            {
                Endpoints = new[] {
                    //new MetricEndpoint("DataDog Cloud", new DataDogMetricHandler("https://api.datadoghq.com/", "{API_KEY}", "{APP_KEY}")),
                    //new MetricEndpoint("Bosun", new BosunMetricHandler("http://devbosun.ds.stackexchange.com/")),
                    //new MetricEndpoint("Test", new TestMetricsHandler("http://127.0.0.1/")),
                    //new MetricEndpoint("DataDog Agent", new DataDogStatsdMetricHandler("dogstatsd.datadog.svc.ny-intkube.k8s", 8125)),
                    //new MetricEndpoint("SignalFx Cloud", new SignalFxMetricHandler("https://ingest.us1.signalfx.com/", "{API_KEY}")),
                    new MetricEndpoint("SignalFx Agent", new SignalFxMetricHandler("http://sfxgateway.signalfx.svc.ny-intkube.k8s:18080")),
                    //new MetricEndpoint(LocalEndpointKey, localHandler)
                },
                MetricsNamePrefix = "scratch3.",
                ThrowOnPostFail = true,
                SnapshotInterval = TimeSpan.FromSeconds(5),
                PropertyToTagName = NameTransformers.CamelToLowerSnakeCase,
                TagValueConverter = (name, value) => name == "converted" ? value.ToLowerInvariant() : value,
                DefaultTags = new Dictionary<string, string> { {"host", NameTransformers.Sanitize(Environment.MachineName.ToLower())} }
            };

            var collector = new MetricsCollector(options);

            collector.BeforeSerialization += () => Console.WriteLine("BosunReporter: Running metrics snapshot.");
            collector.AfterSerialization += info => Console.WriteLine($"BosunReporter: Metric Snapshot took {info.Duration.TotalMilliseconds.ToString("0.##")}ms");
            collector.AfterSend += info =>
            {
                if (info.Endpoint == LocalEndpointKey)
                {
                    foreach (var reading in localHandler.GetReadings())
                    {
                        Console.WriteLine($"{reading.Name}{reading.Suffix}@{reading.Timestamp:s} {reading.Value}");
                    }
                }
                Console.WriteLine($"BosunReporter: {info.BytesWritten} bytes posted to endpoint {info.Endpoint} in {info.Duration.TotalMilliseconds.ToString("0.##")}ms ({(info.Successful ? "SUCCESS" : "FAILED")})");
            };

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

            var gauge2 = collector.GetMetric<CountGauge>("gauge2", "newtons", "Number of newtons currently applied.");
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
            var noHost = collector.CreateMetric<ExcludeHostCounter>("no_host", "units", "Shouldn't have a host tag.");

            var externalCounter = collector.GetMetricGroup<SomeEnum, TestExternalCounter>("external.test", "units", "Should aggregate externally.");
            externalCounter.PopulateFromEnum();
//            var externalNoTags = collector.CreateMetric<ExternalNoTagsCounter>("external.no_tags", "units", "Shouldn't have any tags except relay.");

            var sai = 0;
            var random = new Random();

            s_cancellationTokenSource = new CancellationTokenSource();
            s_samplerTask = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(1000);

                    sampler.Record(++sai % 35);
                    eventGauge.Record(sai % 35);
                    group["low"].Record(random.Next(0, 10));
                    group["medium"].Record(random.Next(10, 20));
                    group["high"].Record(random.Next(20, 30));

                    enumCounter[SomeEnum.One].Increment();
                    enumCounter[SomeEnum.Two].Increment(2);
                    enumCounter[SomeEnum.Three].Increment(3);
                    enumCounter[SomeEnum.Four].Increment(4);

                    externalCounter[SomeEnum.One].Increment();
                    if (sai % 2 == 0)
                        externalCounter[SomeEnum.Two].Increment();
                    if (sai % 3 == 0)
                        externalCounter[SomeEnum.Three].Increment();
                    if (sai % 4 == 0)
                        externalCounter[SomeEnum.Four].Increment();

                    //                    externalNoTags.Increment();

                    converted.Increment();
                    noHost.Increment();

                    if (sai == 100 || s_cancellationTokenSource.IsCancellationRequested)
                    {
                        await collector.ShutdownAsync();
                        break;
                    }
                }
            });

            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
            {
                s_cancellationTokenSource.Cancel();
            };

            await s_samplerTask;
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


    class TestHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = await request.Content.ReadAsStringAsync();
            Console.WriteLine($"Sending metrics to {request.RequestUri}. JSON = {json}");
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            };
        }
    }

    class TestMetricsHandler : BosunMetricHandler
    {
        public TestMetricsHandler(string url) : base(url)
        {
        }

        protected override HttpClient CreateHttpClient() => new HttpClient(new TestHttpHandler());
    }

    [GaugeAggregator(AggregateMode.Count)]
    public class CountGauge : AggregateGauge
    {
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

    public class TestSnapshotGauge : SnapshotGauge
    {
        [BosunTag] public readonly string Thing;

        public TestSnapshotGauge(Func<double> getValue) : base(() => getValue())
        {
            Thing = "nothing";
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
        [BosunTag] public readonly SomeEnum TagName;

        public EnumCounter(SomeEnum val)
        {
            TagName = val;
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

    [ExcludeDefaultTags("host")]
    public class ExcludeHostCounter : Counter
    {
        [BosunTag] public readonly string Other;

        public ExcludeHostCounter()
        {
            Other = "true";
        }
    }

    public class TestExternalCounter : ExternalCounter
    {
        [BosunTag] public readonly SomeEnum Something;

        public TestExternalCounter(SomeEnum something)
        {
            Something = something;
        }
    }

    public class ExternalNoTagsCounter : ExternalCounter
    {
    }
}
