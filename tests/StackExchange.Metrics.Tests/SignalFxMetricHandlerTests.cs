using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    public class SignalFxMetricHandlerTests
    {
        [Fact]
        public void NullUri_DoesNotThrow()
        {
            new SignalFxMetricHandler(null);
        }

        [Fact]
        public void NullUri_Serialization_DoesNotThrow()
        {
            var handler = new SignalFxMetricHandler(null);
            handler.SerializeMetric(new MetricReading("test.metric", MetricType.Counter, 1d, ImmutableDictionary<string, string>.Empty, DateTime.UtcNow));
        }

        [Fact]
        public async Task NullUri_Flush_DoesNotThrow()
        {
            var handler = new SignalFxMetricHandler(null);
            handler.SerializeMetric(new MetricReading("test.metric", MetricType.Counter, 1d, ImmutableDictionary<string, string>.Empty, DateTime.UtcNow));
            await handler.FlushAsync(TimeSpan.Zero, 0, _ => { }, _ => { });
        }

        //[Fact]
        //public async Task UdpUri_Flush_ReceivesStatsD(int maxPayloadSize)
        //{
        //    var port = 5643;
        //    var handler = new SignalFxMetricHandler(new Uri("udp://127.0.0.1:" + port));
        //    var statsdTest = new StatsdMetricHandlerTests();
        //    statsdTest.UdpUri_Flush_ReceivesStatsD()
        //    var cancellationTokenSource = new CancellationTokenSource();
        //    var cancellationToken = cancellationTokenSource.Token;
        //    var listenerTask = Task.Run(
        //        () =>
        //        {
        //            var udpClient = new UdpClient(port);
        //            var ipEndpoint = new IPEndPoint(IPAddress.Loopback, 0);
        //            cancellationToken.Register(() => udpClient.Dispose());
        //            while (!cancellationToken.IsCancellationRequested)
        //            {
        //                try
        //                {
        //                    var bytes = udpClient.Receive(ref ipEndpoint);

        //                    Console.Write(Encoding.UTF8.GetString(bytes));
        //                }
        //                catch { }
        //            }
        //        });


        //    await handler.FlushAsync(TimeSpan.Zero, 0, _ => { }, _ => { });
        //    await Task.Delay(1000);

        //    // make sure the data we received matches the metrics we sent!
        //}
    }
}
