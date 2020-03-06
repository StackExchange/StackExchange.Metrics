using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    public class MetricsCollectorTests
    {
        [Fact]
        public async Task FailedSend_OnlyAffectsFailedHandler()
        {
            var completionEvent = new ManualResetEventSlim(false);
            var failHandler = new TestMetricHandler(
                (_, __, ___) => throw new MetricPostException(new Exception("Boom!"))
            )
            {
                MaxPayloadCount = 10
            };

            var successHandler = new TestMetricHandler(
                async (handler, type, sequence) =>
                {
                    if (type != PayloadType.Counter)
                    {
                        return;
                    }

                    Assert.Equal(PayloadType.Counter, type);
                    using (var memoryStream = new MemoryStream())
                    {
                        await memoryStream.WriteAsync(sequence);

                        // verify that the bytes written to the stream match what we received
                        var actualBytes = memoryStream.ToArray();
                        var expectedBytes = handler.GetNextWrittenChunk(PayloadType.Counter);
                        while (expectedBytes.Length < actualBytes.Length)
                        {
                            // collect bytes until we have no more to get
                            expectedBytes = expectedBytes.Concat(handler.GetNextWrittenChunk(PayloadType.Counter)).ToArray();
                        }

                        Assert.Equal(expectedBytes, actualBytes);
                    }

                    if (!handler.HasPendingChunks(PayloadType.Counter))
                    {
                        completionEvent.Set();
                    }
                }
            )
            {
                MaxPayloadCount = 1000
            };

            var collector = new MetricsCollector(
                new MetricsCollectorOptions(_ => { })
                {
                    DefaultTags = new Dictionary<string, string>
                    {
                        ["host"] = Environment.MachineName
                    },
                    Endpoints = new[]
                    {
                        new MetricEndpoint("Failed", failHandler),
                        new MetricEndpoint("Success", successHandler)
                    },
                    SnapshotInterval = TimeSpan.FromMilliseconds(20),
                    RetryInterval = TimeSpan.Zero
                }
            );

            var metrics = new Counter[10];
            for (var i = 0; i < metrics.Length; i++)
            {
                metrics[i] = collector.CreateMetric<Counter>("metric_" + i, "requests", string.Empty);
            }

            // report some metrics
            foreach (var metric in metrics)
            {
                metric.Increment();
                // waiting for the snapshot interval before we send out next batch of data
                await Task.Delay(20);
            }

            // give some time for everything to complete
            var completed = completionEvent.Wait(TimeSpan.FromMilliseconds(2000));
            var pendingEvents = successHandler.GetPendingChunks(PayloadType.Counter);

            collector.Shutdown();
            Assert.True(completed, $"Success handler did not complete successfully. {pendingEvents} pending");
        }
    }
}
