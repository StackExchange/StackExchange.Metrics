using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Tests
{
    public class TestMetricHandler : BufferedMetricHandler
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

        public int GetPendingChunks(PayloadType type)
        {
            if (!_writtenChunks.TryGetValue(type, out var pendingChunks))
            {
                return -1;
            }

            return pendingChunks.Count;
        }

        public bool HasPendingChunks(PayloadType type) => _writtenChunks.TryGetValue(type, out var pendingChunks) && pendingChunks.Count > 0;

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
