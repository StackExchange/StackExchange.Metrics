using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            var metadata = new MetaData[1000];
            for (var i = 0; i < metadata.Length; i++)
            {
                metadata[i] = new MetaData("test.metric_" + i, "desc", new Dictionary<string, string>(), "This is metadata!");
            }

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
            var utcNow = DateTime.UtcNow;
            var readings = new MetricReading[1000];
            for (var i = 0; i < readings.Length; i++)
            {
                readings[i] = new MetricReading("test.metric_" + i, MetricType.Counter, string.Empty, i, new Dictionary<string, string>(),  utcNow.AddSeconds(i));
            }

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
            var utcNow = DateTime.UtcNow;
            var readings = new MetricReading[1000];
            for (var i = 0; i < readings.Length; i++)
            {
                readings[i] = new MetricReading("test.metric_" + i, MetricType.Counter, string.Empty, i, new Dictionary<string, string>(), utcNow.AddSeconds(i));
            }

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

        private class TestMetricHandler : BufferedMetricHandler
        {
            private readonly Dictionary<PayloadType, Queue<List<byte>>> _writtenChunks;
            private readonly Func<TestMetricHandler, PayloadType, ReadOnlySequence<byte>, ValueTask> _sendHandler;

            public TestMetricHandler(
                Func<TestMetricHandler, PayloadType, ReadOnlySequence<byte>, ValueTask> sendHandler
            )
            {
                _sendHandler = sendHandler;
                _writtenChunks = new Dictionary<PayloadType, Queue<List<byte>>>();
            }

            public byte[] GetNextWrittenChunk(PayloadType type) => _writtenChunks.GetValueOrDefault(type, new Queue<List<byte>>(0)).Dequeue().ToArray();

            private List<byte> GetNextChunk(PayloadType type)
            {
                if (!_writtenChunks.TryGetValue(type, out var writtenChunks))
                {
                    _writtenChunks[type] = writtenChunks = new Queue<List<byte>>();
                }

                var writtenChunk = new List<byte>();
                writtenChunks.Enqueue(writtenChunk);
                return writtenChunk;
            }

            protected override void PrepareSequence(ref ReadOnlySequence<byte> sequence, PayloadType payloadType)
            {
            }

            protected override ValueTask SendAsync(PayloadType type, ReadOnlySequence<byte> sequence) => _sendHandler(this, type, sequence);

            protected override void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<MetaData> metadata)
            {
                var chunk = GetNextChunk(PayloadType.Metadata);

                void Write(string value)
                {
                    var bytes = Encoding.UTF8.GetBytes(value);

                    chunk.AddRange(bytes);
                    writer.Write(bytes);
                }

                foreach (var m in metadata)
                {
                    Write(m.Metric);
                    Write("|");
                    Write(m.Name);
                    Write("|");
                    Write(m.Value);
                    Write(Environment.NewLine);
                }
            }

            protected override void SerializeMetric(IBufferWriter<byte> writer, in MetricReading reading)
            {
                var chunk = GetNextChunk(PayloadType.Counter);
                var bytes = Encoding.UTF8.GetBytes(SerializeMetric(reading));
                chunk.AddRange(bytes);
                writer.Write(bytes);
            }

            protected new virtual string SerializeMetric(in MetricReading reading)
            {
                var sb = new StringBuilder();
                sb.Append(reading.NameWithSuffix);
                sb.Append('|');
                sb.Append(reading.Value);
                sb.Append('|');
                sb.Append(reading.Timestamp.ToString("s"));
                sb.Append(Environment.NewLine);
                return sb.ToString();
            }
        }
    }
}
