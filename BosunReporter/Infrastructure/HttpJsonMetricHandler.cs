using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BosunReporter.Infrastructure
{
    /// <summary>
    /// Abstract implementation of <see cref="BufferedMetricHandler" /> that posts JSON
    /// to an HTTP endpoint.
    /// </summary>
    public abstract class HttpJsonMetricHandler : BufferedMetricHandler
    {
        private readonly Lazy<HttpClient> _httpClientFactory;

        /// <summary>
        /// Constructs an instance of <see cref="HttpJsonMetricHandler" /> that sends metric data using an <see cref="HttpClient" />.
        /// </summary>
        protected HttpJsonMetricHandler()
        {
            _httpClientFactory = new Lazy<HttpClient>(CreateHttpClient);
        }

        /// <inheritdoc />
        protected override ValueTask SendAsync(PayloadType payloadType, ReadOnlyMemory<byte> buffer)
        {
            switch (payloadType)
            {
                case PayloadType.Metadata:
                    return SendMetadataAsync(buffer);
                case PayloadType.Counter:
                    return SendCounterAsync(buffer);
                case PayloadType.CumulativeCounter:
                    return SendCumulativeCounterAsync(buffer);
                case PayloadType.Gauge:
                    return SendGaugeAsync(buffer);
                default:
                    throw new ArgumentOutOfRangeException(nameof(payloadType));
            }
        }

        /// <summary>
        /// Sends a buffer containing counters to an HTTP endpoint.
        /// </summary>
        /// <param name="buffer">
        /// <see cref="ReadOnlyMemory{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendCounterAsync(ReadOnlyMemory<byte> buffer);

        /// <summary>
        /// Sends a buffer containing cumulative counter metrics to an HTTP endpoint.
        /// </summary>
        /// <param name="buffer">
        /// <see cref="ReadOnlyMemory{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendCumulativeCounterAsync(ReadOnlyMemory<byte> buffer);

        /// <summary>
        /// Sends a buffer containing gauge metrics to an HTTP endpoint.
        /// </summary>
        /// <param name="buffer">
        /// <see cref="ReadOnlyMemory{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendGaugeAsync(ReadOnlyMemory<byte> buffer);

        /// <summary>
        /// Sends a buffer containing metadata to an HTTP endpoint.
        /// </summary>
        /// <param name="buffer">
        /// <see cref="ReadOnlyMemory{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendMetadataAsync(ReadOnlyMemory<byte> buffer);

        /// <summary>
        /// Gets the length of the preamble for a given type of payload.
        /// </summary>
        /// <param name="payloadType">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        protected abstract int GetPreambleLength(PayloadType payloadType);

        /// <summary>
        /// Writes a preamble to the underlying HTTP stream.
        /// </summary>
        /// <param name="stream">Stream to write to.</param>
        /// <param name="payloadType">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        protected abstract Task WritePreambleAsync(Stream stream, PayloadType payloadType);

        /// <summary>
        /// Gets the length of the postamble for a given type of payload
        /// </summary>
        /// <param name="payloadType">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        protected abstract int GetPostambleLength(PayloadType payloadType);

        /// <summary>
        /// Writes a postamble to the underlying HTTP stream.
        /// </summary>
        /// <param name="stream">Stream to write to.</param>
        /// <param name="payloadType">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        protected abstract Task WritePostambleAsync(Stream stream, PayloadType payloadType);

        /// <summary>
        /// Prepares a buffer for writing to the underlying HTTP stream.
        /// </summary>
        /// <param name="buffer">
        /// <see cref="ReadOnlyMemory{T}"/> representing buffer.
        /// </param>
        /// <param name="payloadType">
        /// A <see cref="PayloadType" /> value.
        /// </param>
        protected abstract void PrepareBuffer(ref ReadOnlyMemory<byte> buffer, PayloadType payloadType);

        /// <summary>
        /// Creates an <see cref="HttpClient" /> used for sending data to the endpoint.
        /// </summary>
        protected virtual HttpClient CreateHttpClient() => new HttpClient();

        /// <inheritdoc />
        protected async ValueTask SendAsync(Uri uri, HttpMethod method, PayloadType payloadType, ReadOnlyMemory<byte> buffer)
        {
            PrepareBuffer(ref buffer, payloadType);

            var preambleLength = GetPreambleLength(payloadType);
            var postambleLength = GetPostambleLength(payloadType);
            var request = new HttpRequestMessage(method, uri)
            {
                Content = new ReadOnlyMemoryContent(payloadType, buffer, preambleLength, WritePreambleAsync, postambleLength, WritePostambleAsync)
            };

            var response = await _httpClientFactory.Value.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                string errorText = string.Empty;
                try
                {
                    errorText = await response.Content.ReadAsStringAsync();
                }
                catch
                {
                    // nothing we can do here
                }

                throw new HttpRequestException(
                    $"Response status code does not indicate success: {response.StatusCode}. {errorText}"
                );
            }
        }

        private class ReadOnlyMemoryContent : HttpContent
        {
            readonly PayloadType _type;
            readonly ReadOnlyMemory<byte> _memory;
            readonly Func<Stream, PayloadType, Task> _writePreamble;
            readonly Func<Stream, PayloadType, Task> _writePostamble;
            readonly int _length;

            public ReadOnlyMemoryContent(
                PayloadType type,
                ReadOnlyMemory<byte> memory, 
                int preambleLength,
                Func<Stream, PayloadType, Task> writePreamble, 
                int postambleLength,
                Func<Stream, PayloadType, Task> writePostamble
            )
            {
                _type = type;
                _memory = memory;
                _length = memory.Length + preambleLength + postambleLength;
                _writePreamble = writePreamble;
                _writePostamble = writePostamble;

                Headers.ContentType = s_jsonHeader;
            }

            static readonly MediaTypeHeaderValue s_jsonHeader = new MediaTypeHeaderValue("application/json");

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                await _writePreamble(stream, _type);

                // TODO: for netcoreapp2.2 this should be writing the ReadOnlyMemory directly to the stream
                // for now we try to get the underlying buffer from the ReadOnlyMemory using MemoryMarshal
                // which works on old versions of the framework
                if (MemoryMarshal.TryGetArray(_memory, out var segment))
                {
                    await stream.WriteAsync(segment.Array, segment.Offset, segment.Count);
                }
                else
                {
                    // we can't get the buffer directly, copy the ReadOnlyMemory to a new one instead
                    // grab a buffer that we can prefix with an array start and suffix with an array end
                    var buffer = ArrayPool<byte>.Shared.Rent(_memory.Length);
                    try
                    {
                        _memory.CopyTo(buffer.AsMemory());
                        await stream.WriteAsync(buffer, 0, _memory.Length);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                await _writePostamble(stream, _type);
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _length;
                return false;
            }
        }
    }
}
