using System;
using System.Collections.Generic;
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
            // delay because UDP gets all weird otherwise
            await Task.Delay(200);

            var port = (ushort)_rng.Next(1024, 65535);
            var handler = new StatsdMetricHandler("127.0.0.1", port);
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                var utcNow = DateTime.UtcNow;
                var reading = new MetricReading("test.metrics", MetricType.Counter, string.Empty, value, new Dictionary<string, string> { ["host"] = "test!" }, utcNow);
                var listenerTask = ReceiveStatsdOverUdpAsync(port, cancellationTokenSource.Token, _output);

                // expected format: {metric}:{value}|{unit}|#{tag},{tag}
                // so for this counter: "test.metrics:1|c|#host:test!
                handler.SerializeMetric(reading);
                await handler.FlushAsync(
                    TimeSpan.Zero, 0, e => _output.WriteLine($"{e.BytesWritten} bytes written"), ex => _output.WriteLine(ex.ToString())
                );

                cancellationTokenSource.Cancel();

                // make sure the data we received matches the statsd for the bytes we sent!
                var actualBytes = await listenerTask;
                var expectedBytes = ToStatsd(reading);

                _output.WriteLine($"Actual:\t\t{string.Join(" ", actualBytes.Select(x => $"{x:x2}"))}");
                _output.WriteLine($"Expected:\t{string.Join(" ", expectedBytes.Select(x => $"{x:x2}"))}");

                Assert.Equal(expectedBytes, actualBytes);
            }
        }

        public static byte[] ToStatsd(in MetricReading reading)
        {
            string ToTypeString(MetricType type) =>
                type switch
                {
                    MetricType.Counter => "c",
                    MetricType.CumulativeCounter => "c",
                    MetricType.Gauge => "g",
                };

            string ToTagString(IReadOnlyDictionary<string, string> tags) => string.Join(",", tags.Select(t => t.Key + ":" + t.Value));

            return Encoding.UTF8.GetBytes(
                reading.NameWithSuffix + ":" + (reading.Value % 1 == 0 ? reading.Value.ToString("0") : reading.Value.ToString("0.00000")) + "|" + ToTypeString(reading.Type) + "|" + "#" + ToTagString(reading.Tags)
            );
        }

        public static async Task<byte[]> ReceiveStatsdOverUdpAsync(ushort port, CancellationToken cancellationToken, ITestOutputHelper output)
        {
            var bytesReceived = new List<byte>();
            await Task.Run(
                () =>
                {
                    var localEndpoint = new IPEndPoint(IPAddress.Loopback, port);
                    using (var udpClient = new UdpClient(localEndpoint))
                    {
                        udpClient.Client.ReceiveTimeout = 200;

                        while (!cancellationToken.IsCancellationRequested)
                        {
                            var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 0);
                            try
                            {
                                output.WriteLine($"Listening on {localEndpoint}");
                                bytesReceived.AddRange(udpClient.Receive(ref remoteEndpoint));
                                output.WriteLine($"Received {bytesReceived.Count} bytes from {remoteEndpoint}");
                            }
                            catch (Exception ex)
                            {
                                output.WriteLine(ex.ToString());
                            }
                        }
                    }
                });

            return bytesReceived.ToArray();
        }
    }
}
