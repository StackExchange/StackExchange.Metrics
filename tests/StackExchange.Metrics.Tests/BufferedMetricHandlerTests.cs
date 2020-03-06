using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Metrics.Infrastructure;
using Xunit;

namespace StackExchange.Metrics.Tests
{
    // tests that ensure the buffering and buffer manipulation aspects of the
    // buffered metric handler work correctly
    public class BufferedMetricHandlerTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public async Task SerializeMetadata_WritesToSendBuffer(int maxPayloadSize)
        {
            var metadata = TestHelper.GenerateMetadata(1000);
            var handler = new TestMetricHandler(
                async (handler, type, sequence) =>
                {
                    Assert.Equal(PayloadType.Metadata, type);
                    using (var memoryStream = new MemoryStream())
                    {
                        await memoryStream.WriteAsync(sequence);

                        // verify that the bytes written to the stream match what we received
                        var actualBytes = memoryStream.ToArray();
                        var expectedBytes = handler.GetNextWrittenChunk(PayloadType.Metadata);
                        while (expectedBytes.Length < actualBytes.Length)
                        {
                            // collect bytes until we have no more to get
                            expectedBytes = expectedBytes.Concat(handler.GetNextWrittenChunk(PayloadType.Metadata)).ToArray();
                        }

                        Assert.Equal(expectedBytes, actualBytes);
                    }
                })
            {
                MaxPayloadSize = maxPayloadSize,
                MaxPayloadCount = 1000
            };

            handler.SerializeMetadata(metadata);
            await handler.FlushAsync(TimeSpan.Zero, 0, _ => { }, _ => { });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public async Task SerializeMetric_WritesToSendBuffer(int maxPayloadSize)
        {
            var readings = TestHelper.GenerateReadings(1000);
            var handler = new TestMetricHandler(
                async (handler, type, sequence) =>
                {
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
                })
            {
                MaxPayloadSize = maxPayloadSize,
                MaxPayloadCount = 1000
            };

            foreach (var reading in readings)
            {
                handler.SerializeMetric(reading);
            }

            await handler.FlushAsync(TimeSpan.Zero, 0, _ => { }, _ => { });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public async Task PrepareSequence_AltersSendBuffer(int maxPayloadSize)
        {
            var readings = TestHelper.GenerateReadings(1000);
            var handler = new TestWithPrepareSequence(
                async (handler, type, sequence) =>
                {
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

                        Assert.NotEqual((byte)',', actualBytes.Last());
                    }
                })
            {
                MaxPayloadSize = maxPayloadSize,
                MaxPayloadCount = 1000
            };

            foreach (var reading in readings)
            {
                handler.SerializeMetric(reading);
            }

            await handler.FlushAsync(TimeSpan.Zero, 0, _ => { }, _ => { });
        }

        private class TestWithPrepareSequence : TestMetricHandler
        {
            public TestWithPrepareSequence(
                Func<TestMetricHandler, PayloadType, ReadOnlySequence<byte>, ValueTask> sendHandler
            ) : base(sendHandler)
            {
            }

            protected override void PrepareSequence(ref ReadOnlySequence<byte> sequence, PayloadType payloadType)
            {
                sequence = sequence.Trim(',');
            }

            protected override string SerializeMetric(in MetricReading reading)
            {
                return base.SerializeMetric(reading) + ",";
            }
        }
    }
}
