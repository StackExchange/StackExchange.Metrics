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

        readonly Dictionary<PayloadType, PayloadTypeMetadata> _payloadMetadata;

        /// <summary>
        /// Constructs a <see cref="BufferedMetricHandler" />.
        /// </summary>
        protected BufferedMetricHandler()
        {
            _payloadMetadata = new Dictionary<PayloadType, PayloadTypeMetadata>();
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

        /// <ineritdoc />
        public IMetricBatch BeginBatch() => new Batch(this);

        private void SerializeMetric(PayloadType payloadType, PayloadTypeMetadata payloadMetadata, in MetricReading reading)
        {
            if (payloadMetadata.PayloadCount >= MaxPayloadCount)
            {
                throw new BosunQueueFullException(payloadType, payloadMetadata.PayloadCount);
            }

            var bufferWriter = payloadMetadata.BufferWriter;
            var startPosition = bufferWriter.Length;
            var flushPosition = payloadMetadata.FlushPositions.LastOrDefault();
            SerializeMetric(bufferWriter, reading);
            var endPosition = bufferWriter.Length;
            if (endPosition - flushPosition > MaxPayloadSize && startPosition > 0)
            {
                payloadMetadata.FlushPositions.Add(startPosition);
            }

            payloadMetadata.PayloadCount++;
        }

        /// <summary>
        /// Serializes a metric into a format appropriate for the endpoint.
        /// </summary>
        /// <param name="reading">
        /// A <see cref="MetricReading" /> containing the metric to serialize.
        /// </param>
        public void SerializeMetric(in MetricReading reading)
        {
            var payloadType = GetPayloadType(reading.Type);
            var payloadMetadata = GetPayloadTypeMetadata(payloadType);

            SerializeMetric(payloadType, payloadMetadata, reading);
        }

        /// <summary>
        /// Serializes metadata about available metrics into a format appropriate for the endpoint.
        /// </summary>
        /// <param name="metadata">
        /// An <see cref="IEnumerable{T}" /> of <see cref="MetaData" /> representing the metadata to persist.
        /// </param>
        public void SerializeMetadata(IEnumerable<MetaData> metadata)
        {
            var payloadMetadata = GetPayloadTypeMetadata(PayloadType.Metadata);
            var bufferWriter = payloadMetadata.BufferWriter;
            // keep track of how much data was written
            var startPosition = bufferWriter.Length;
            SerializeMetadata(bufferWriter.Writer, metadata);
            var endPosition = bufferWriter.Length;
            if (endPosition > MaxPayloadSize)
            {
                payloadMetadata.FlushPositions.Add(startPosition);
            }
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
            async Task SendWithErrorHandlingAsync(PayloadType payloadType, ReadOnlySequence<byte> sequence)
            {
                var info = new AfterSendInfo { PayloadType = payloadType };
                var sw = Stopwatch.StartNew();
                try
                {
                    PrepareSequence(ref sequence, payloadType);
                    if (sequence.Length > 0)
                    {
                        await SendAsync(payloadType, sequence);
                    }
                    info.BytesWritten = sequence.Length;
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
                var payloadMetadata = GetPayloadTypeMetadata(payloadType);
                var bufferWriter = payloadMetadata.BufferWriter;
                if (bufferWriter.Length == 0)
                {
                    payloadMetadata.PayloadCount = 0;
                    payloadMetadata.FlushPositions.Clear();
                    continue;
                }

                using (var data = bufferWriter.Flush())
                {
                    var sequence = data.Value;
                    while (true)
                    {
                        try
                        {
                            // flush blocks based upon the positions we recorded, or everything
                            // if there are no positions recorded
                            var positionsToFlush = payloadMetadata.FlushPositions.ToList();
                            if (positionsToFlush.Count == 0)
                            {
                                // flush everything
                                await SendWithErrorHandlingAsync(payloadType, sequence);
                            }
                            else
                            {
                                // flush each valid payload individually
                                var startIndex = 0L;
                                foreach (var positionToFlush in positionsToFlush)
                                {
                                    await SendWithErrorHandlingAsync(payloadType, sequence.Slice(startIndex, positionToFlush - startIndex));
                                    startIndex = positionToFlush;
                                }

                                if (startIndex < sequence.Length)
                                {
                                    await SendWithErrorHandlingAsync(payloadType, sequence.Slice(startIndex));
                                }
                            }

                            payloadMetadata.PayloadCount = 0;
                            payloadMetadata.FlushPositions.Clear();
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

        /// <inheritdoc />
        public virtual void Dispose()
        {
        }

        /// <summary>
        /// Prepares a sequence for writing to the underlying transport.
        /// </summary>
        /// <param name="sequence">
        /// <see cref="ReadOnlySequence{T}"/> representing buffer.
        /// </param>
        /// <param name="payloadType">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        protected abstract void PrepareSequence(ref ReadOnlySequence<byte> sequence, PayloadType payloadType);

        /// <summary>
        /// Sends a batch of serialized data to the metrics endpoint.
        /// </summary>
        /// <param name="type">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        /// <param name="sequence">
        /// <see cref="ReadOnlySequence{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendAsync(PayloadType type, ReadOnlySequence<byte> sequence);

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

        /// <summary>
        /// Creates a <see cref="PayloadTypeMetadata" /> for a specific type of payload.
        /// </summary>
        /// <param name="payloadType">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        /// <remarks>
        /// Overriding implementations can return the same <see cref="PayloadTypeMetadata" />
        /// instance for different kinds of payload.
        /// </remarks>
        protected virtual PayloadTypeMetadata CreatePayloadTypeMetadata(PayloadType payloadType) 
            => new PayloadTypeMetadata(BufferWriter<byte>.Create(blockSize: MaxPayloadSize));

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

        private PayloadTypeMetadata GetPayloadTypeMetadata(PayloadType payloadType)
        {
            if (!_payloadMetadata.TryGetValue(payloadType, out var metadata))
            {
                metadata = _payloadMetadata[payloadType] = CreatePayloadTypeMetadata(payloadType);
            }

            return metadata;
        }

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
                var payloadMetadata = _handler.GetPayloadTypeMetadata(payloadType);
                var bufferWriter = payloadMetadata.BufferWriter;
                var startBytes = bufferWriter.Length;
                _handler.SerializeMetric(payloadType, payloadMetadata, reading);
                MetricsWritten++;
                BytesWritten += bufferWriter.Length - startBytes;
            }

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Wraps a <see cref="BufferWriter{T}" /> to keep track of the payload count
        /// and completed payloads offsets.
        /// </summary>
        protected class PayloadTypeMetadata
        {
            /// <summary>
            /// Constructs a new instance of <see cref="PayloadTypeMetadata" />.
            /// </summary>
            /// <param name="bufferWriter">
            /// A <see cref="BufferWriter{T}" /> used for serialized payloads.
            /// </param>
            public PayloadTypeMetadata(BufferWriter<byte> bufferWriter)
            {
                BufferWriter = bufferWriter;
                FlushPositions = new List<long>();
                PayloadCount = 0;
            }

            internal BufferWriter<byte> BufferWriter { get; }
            internal int PayloadCount { get; set; }
            internal List<long> FlushPositions { get; }
        }
    }
}