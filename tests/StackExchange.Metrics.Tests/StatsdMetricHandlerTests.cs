using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Metrics.Tests
{
    public class StatsdMetricHandlerTests
    {
        readonly Random _rng;
        readonly ITestOutputHelper _output;

        public StatsdMetricHandlerTests(ITestOutputHelper output)
        {
            _rng = new Random();
            _output = output;
        }

        [Theory]
        [InlineData(1d)]
        [InlineData(2.443d)]
        [InlineData(1.1234457d)]
        [InlineData(9.1234457d)]
        public async Task UdpUri_Counter_ReceivesValidStatsd(double value)
        {
            var port = (ushort)_rng.Next(1024, 65535);
            var handler = new StatsdMetricHandler("127.0.0.1", port);
            var utcNow = DateTime.UtcNow;
            var reading = new MetricReading("test.metrics", MetricType.Counter, value, new Dictionary<string, string> { ["host"] = "test!" }.ToImmutableDictionary(), utcNow);
            var listenerTask = ReceiveStatsdOverUdpAsync(port, _output);

            // expected format: {metric}:{value}|{unit}|#{tag},{tag}
            // so for this counter: "test.metrics:1|c|#host:test!
            handler.SerializeMetric(reading);
            await handler.FlushAsync(
                TimeSpan.Zero, 0, e => _output.WriteLine($"{e.BytesWritten} bytes written"), ex => _output.WriteLine(ex.ToString())
            );

            // make sure the data we received matches the statsd for the bytes we sent!
            var actualBytes = await listenerTask;
            var expectedBytes = ToStatsd(reading);

            _output.WriteLine($"Actual:\t\t{string.Join(" ", actualBytes.Select(x => $"{x:x2}"))}");
            _output.WriteLine($"Expected:\t{string.Join(" ", expectedBytes.Select(x => $"{x:x2}"))}");

            Assert.Equal(expectedBytes, actualBytes);
        }

        public static byte[] ToStatsd(in MetricReading reading)
        {
            static string ToTypeString(MetricType type) =>
                type switch
                {
                    MetricType.Counter => "c",
                    MetricType.CumulativeCounter => "c",
                    MetricType.Gauge => "g",
                    _ => "?",
                };

            static string ToTagString(IReadOnlyDictionary<string, string> tags) => string.Join(",", tags.Select(t => t.Key + ":" + t.Value));

            return Encoding.UTF8.GetBytes(
                reading.Name + ":" + (reading.Value % 1 == 0 ? reading.Value.ToString("0") : reading.Value.ToString("0.00000")) + "|" + ToTypeString(reading.Type) + "|" + "#" + ToTagString(reading.Tags)
            );
        }

        public static Task<byte[]> ReceiveStatsdOverUdpAsync(ushort port, ITestOutputHelper output)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            var resetEvent = new ManualResetEventSlim(false);
            Task.Run(
                () =>
                {
                    output.WriteLine("Starting UdpClient");

                    var localEndpoint = new IPEndPoint(IPAddress.Loopback, port);
                    using (var udpClient = new UdpClient(localEndpoint))
                    {
                        udpClient.Client.ReceiveTimeout = 500;

                        var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 0);
                        try
                        {
                            resetEvent.Set();
                            output.WriteLine($"Listening on {localEndpoint}");
                            var buffer = udpClient.Receive(ref remoteEndpoint);
                            output.WriteLine($"Received {buffer.Length} bytes from {remoteEndpoint}");
                            if (buffer.Length > 0)
                            {
                                tcs.SetResult(buffer);
                            }
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    }
                });

            resetEvent.Wait();
            return tcs.Task;
        }

        [Fact]
        public async Task DeactivatedUdpUri_Counter_ReceivesNoStatsd()
        {
            const ushort port = 1234;
            var handler = new StatsdMetricHandler(null, port);
            var utcNow = DateTime.UtcNow;
            var reading = new MetricReading("test.metrics", MetricType.Counter, _rng.Next(), new Dictionary<string, string> { ["host"] = "test!" }.ToImmutableDictionary(), utcNow);

            handler.SerializeMetric(reading);

            void AfterSendAssertion(AfterSendInfo i)
            {
                Assert.Equal(0, i.BytesWritten);
                Assert.Equal("", i.Endpoint);
                Assert.True(i.Successful);
            }

            await handler.FlushAsync(
                TimeSpan.Zero, 0, AfterSendAssertion, ex => _output.WriteLine(ex.ToString())
            );
        }
    }
}
