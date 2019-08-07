using Pipelines.Sockets.Unofficial.Buffers;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BosunReporter.Infrastructure
{
    /// <summary>
    /// Abstract base class used to serialize and send metrics to an arbitrary backend
    /// by buffering the serialized metrics into a byte array.
    /// </summary>
    public abstract class BufferedMetricHandler : IMetricHandler
    {
        static readonly PayloadType[] s_payloadTypes = (PayloadType[])Enum.GetValues(typeof(PayloadType));

        readonly Lazy<BufferWriter<byte>[]> _bufferWriterFactory;
        readonly Dictionary<PayloadType, int> _payloadCountsByType;

        /// <summary>
        /// Constructs a <see cref="BufferedMetricHandler" />.
        /// </summary>
        protected BufferedMetricHandler()
        {
            _bufferWriterFactory = new Lazy<BufferWriter<byte>[]>(CreateBufferWriters);
            _payloadCountsByType = new Dictionary<PayloadType, int>();
        }

        /// <summary>
        /// The maximum size of a single payload to an endpoint. It's best practice to set this to a size which can fit inside a 
        /// single initial TCP packet window. E.g. 10 x 1400 bytes.
        /// 
        /// HTTP Headers are not included in this size, so it's best to pick a value a bit smaller
        /// than the size of your initial TCP packet window. However, this property cannot be set to a size less than 1000.
        /// </summary>
        public int MaxPayloadSize { get; set; } = 8000;

        /// <summary>
        /// Gets the maximum number of payloads we can keep before we consider our buffers full.
        /// </summary>
        public long MaxPayloadCount { get; set; } = 240;

        /// <summary>
        /// Gets the total size of buffer used by the handler.
        /// </summary>
        public long TotalBufferSize => _bufferWriterFactory.Value.Sum(x => x.Length);

        /// <ineritdoc />
        public IMetricBatch BeginBatch() => new Batch(this);

        /// <summary>
        /// Serializes a metric into a format appropriate for the endpoint.
        /// </summary>
        /// <param name="reading">
        /// A <see cref="MetricReading" /> containing the metric to serialize.
        /// </param>
        public void SerializeMetric(in MetricReading reading)
        {
            var payloadType = GetPayloadType(reading.Type);
            var bufferWriter = GetBufferWriter(payloadType);

            SerializeMetric(bufferWriter.Writer, reading);
        }

        /// <summary>
        /// Serializes metadata about available metrics into a format appropriate for the endpoint.
        /// </summary>
        /// <param name="metadata">
        /// An <see cref="IEnumerable{T}" /> of <see cref="MetaData" /> representing the metadata to persist.
        /// </param>
        public void SerializeMetadata(IEnumerable<MetaData> metadata)
        {
            var bufferWriter = GetBufferWriter(PayloadType.Metadata);

            SerializeMetadata(bufferWriter.Writer, metadata);
        }

        /// <summary>
        /// Flushes the buffer for a type of payload to the underlying endpoint.
        /// </summary>
        /// <param name="delayBetweenRetries">
        /// <see cref="TimeSpan" /> between retries.
        /// </param>
        /// <param name="maxRetries">
        /// Maximum number of retries before we fail.
        /// </param>
        /// <param name="afterSend">
        /// Handler used to record metrics.
        /// </param>
        /// <param name="exceptionHandler">
        /// Handler used when to record exception information.
        /// </param>
        public async ValueTask FlushAsync(TimeSpan delayBetweenRetries, int maxRetries, Action<AfterSendInfo> afterSend, Action<Exception> exceptionHandler)
        {
            async Task SendWithErrorHandlingAsync(PayloadType payloadType, ReadOnlyMemory<byte> buffer)
            {
                var info = new AfterSendInfo();
                var sw = Stopwatch.StartNew();
                try
                {
                    await SendAsync(payloadType, buffer);
                    info.BytesWritten = buffer.Length;
                    sw.Stop();
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    info.Exception = ex;
                    exceptionHandler?.Invoke(ex);
                    throw;
                }
                finally
                {
                    info.Duration = sw.Elapsed;
                    afterSend?.Invoke(info);
                }
            }

            foreach (var payloadType in s_payloadTypes)
            {
                var retries = 0;
                var bufferWriter = GetBufferWriter(payloadType);
                if (bufferWriter.Length == 0)
                {
                    _payloadCountsByType[payloadType] = 0;
                    return;
                }

                using (var data = bufferWriter.Flush())
                {
                    var sequence = data.Value;
                    while (true)
                    {
                        try
                        {
                            if (sequence.IsSingleSegment)
                            {
                                await SendWithErrorHandlingAsync(payloadType, sequence.First);
                            }
                            else
                            {
                                foreach (var segment in sequence)
                                {
                                    await SendWithErrorHandlingAsync(payloadType, segment);
                                }
                            }
                            _payloadCountsByType[payloadType] = 0;
                            break;
                        }
                        catch (Exception) when (++retries < maxRetries)
                        {
                            // posting to the endpoint failed
                            // loop until we reach our limit or until we succeed
                            Debug.WriteLine($"BosunReporter: Sending to the endpoint failed. Retry {retries} of {maxRetries}.");
                            await Task.Delay(delayBetweenRetries);
                        }
                        catch (Exception)
                        {
                            Debug.WriteLine($"BosunReporter: Sending to the endpoint failed. Maximum retries reached.");
                            throw;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sends a batch of serialized data to the metrics endpoint.
        /// </summary>
        /// <param name="type">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        /// <param name="buffer">
        /// <see cref="ReadOnlyMemory{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendAsync(PayloadType type, ReadOnlyMemory<byte> buffer);

        /// <summary>
        /// Serializes a metric into the supplied <see cref="IBufferWriter{T}" />
        /// </summary>
        /// <param name="writer">
        /// An <see cref="IBufferWriter{T}"/>.
        /// </param>
        /// <param name="reading">
        /// A <see cref="MetricReading" /> containing the metric to serialize.
        /// </param>
        protected abstract void SerializeMetric(IBufferWriter<byte> writer, in MetricReading reading);

        /// <summary>
        /// Serializes metadata about available metrics into the supplied <see cref="IBufferWriter{T}"/>.
        /// </summary>
        /// <param name="writer">
        /// An <see cref="IBufferWriter{T}"/>.
        /// </param>
        /// <param name="metadata">
        /// An <see cref="IEnumerable{T}" /> of <see cref="MetaData" /> representing the metadata to persist.
        /// </param>

        protected abstract void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<MetaData> metadata);

        /// <summary>
        /// Creates a <see cref="BufferWriter{T}" /> for a specific type of payload.
        /// </summary>
        /// <param name="payloadType">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        /// <remarks>
        /// Overriding implementations can return the same <see cref="BufferWriter{T}" />
        /// instance for different kinds of payload.
        /// </remarks>
        protected virtual BufferWriter<byte> CreateBufferWriter(PayloadType payloadType) => BufferWriter<byte>.Create(blockSize: MaxPayloadSize);

        private static PayloadType GetPayloadType(MetricType metricType)
        {
            switch (metricType)
            {
                case MetricType.Counter:
                    return PayloadType.Counter;
                case MetricType.CumulativeCounter:
                    return PayloadType.CumulativeCounter;
                case MetricType.Gauge:
                    return PayloadType.Gauge;
                default:
                    throw new ArgumentOutOfRangeException(nameof(metricType));
            }
        }

        private BufferWriter<byte> GetBufferWriter(PayloadType payloadType) => _bufferWriterFactory.Value[(int)payloadType];

        private BufferWriter<byte>[] CreateBufferWriters()
        {
            var bufferWriters = new BufferWriter<byte>[s_payloadTypes.Length];
            for (var i = 0; i < s_payloadTypes.Length; i++)
            {
                bufferWriters[i] = CreateBufferWriter(s_payloadTypes[i]);
            }
            return bufferWriters;
        }

        private class Batch : IMetricBatch
        {
            private readonly BufferedMetricHandler _handler;

            public Batch(BufferedMetricHandler handler)
            {
                _handler = handler;
            }

            public long BytesWritten { get; private set; }
            public long MetricsWritten { get; private set; }

            /// <inheritdoc />
            public void SerializeMetric(in MetricReading reading)
            {
                var payloadType = GetPayloadType(reading.Type);
                if (!_handler._payloadCountsByType.TryGetValue(payloadType, out var payloadCount))
                {
                    payloadCount = 0;
                }

                if (payloadCount >= _handler.MaxPayloadCount)
                {
                    throw new BosunQueueFullException(payloadType, payloadCount);
                }

                var bufferWriter = _handler.GetBufferWriter(payloadType);
                var startBytes = bufferWriter.Length;
                _handler.SerializeMetric(bufferWriter.Writer, reading);
                _handler._payloadCountsByType[payloadType] = payloadCount + 1;

                MetricsWritten++;
                BytesWritten += bufferWriter.Length - startBytes;
            }

            public void Dispose()
            {
            }
        }

    }
}