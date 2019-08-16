using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
        public override ValueTask DisposeAsync()
        {
            if (_httpClientFactory.IsValueCreated)
            {
                _httpClientFactory.Value.Dispose();
            }

            return default;
        }

        /// <inheritdoc />
        protected override ValueTask SendAsync(PayloadType payloadType, ReadOnlySequence<byte> sequence)
        {
            switch (payloadType)
            {
                case PayloadType.Metadata:
                    return SendMetadataAsync(sequence);
                case PayloadType.Counter:
                    return SendCounterAsync(sequence);
                case PayloadType.CumulativeCounter:
                    return SendCumulativeCounterAsync(sequence);
                case PayloadType.Gauge:
                    return SendGaugeAsync(sequence);
                default:
                    throw new ArgumentOutOfRangeException(nameof(payloadType));
            }
        }

        /// <summary>
        /// Sends a buffer containing counters to an HTTP endpoint.
        /// </summary>
        /// <param name="sequence">
        /// <see cref="ReadOnlySequence{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendCounterAsync(ReadOnlySequence<byte> sequence);

        /// <summary>
        /// Sends a buffer containing cumulative counter metrics to an HTTP endpoint.
        /// </summary>
        /// <param name="sequence">
        /// <see cref="ReadOnlySequence{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendCumulativeCounterAsync(ReadOnlySequence<byte> sequence);

        /// <summary>
        /// Sends a buffer containing gauge metrics to an HTTP endpoint.
        /// </summary>
        /// <param name="sequence">
        /// <see cref="ReadOnlySequence{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendGaugeAsync(ReadOnlySequence<byte> sequence);

        /// <summary>
        /// Sends a buffer containing metadata to an HTTP endpoint.
        /// </summary>
        /// <param name="sequence">
        /// <see cref="ReadOnlySequence{T}" /> containing the data to send.
        /// </param>
        protected abstract ValueTask SendMetadataAsync(ReadOnlySequence<byte> sequence);

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
        /// Creates an <see cref="HttpClient" /> used for sending data to the endpoint.
        /// </summary>
        protected virtual HttpClient CreateHttpClient() => new HttpClient();

        /// <inheritdoc />
        protected async ValueTask SendAsync(Uri uri, HttpMethod method, PayloadType payloadType, ReadOnlySequence<byte> sequence)
        {
            if (uri == null)
            {
                return;
            }

            var preambleLength = GetPreambleLength(payloadType);
            var postambleLength = GetPostambleLength(payloadType);
            var request = new HttpRequestMessage(method, uri)
            {
                Content = new ReadOnlySequenceContent(payloadType, sequence, preambleLength, WritePreambleAsync, postambleLength, WritePostambleAsync)
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

        private class ReadOnlySequenceContent : HttpContent
        {
            readonly PayloadType _type;
            readonly ReadOnlySequence<byte> _sequence;
            readonly Func<Stream, PayloadType, Task> _writePreamble;
            readonly Func<Stream, PayloadType, Task> _writePostamble;
            readonly long _length;

            public ReadOnlySequenceContent(
                PayloadType type,
                in ReadOnlySequence<byte> sequence, 
                int preambleLength,
                Func<Stream, PayloadType, Task> writePreamble, 
                int postambleLength,
                Func<Stream, PayloadType, Task> writePostamble
            )
            {
                _type = type;
                _sequence = sequence;
                _length = sequence.Length + preambleLength + postambleLength;
                _writePreamble = writePreamble;
                _writePostamble = writePostamble;

                Headers.ContentType = s_jsonHeader;
            }

            static readonly MediaTypeHeaderValue s_jsonHeader = new MediaTypeHeaderValue("application/json");

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                await _writePreamble(stream, _type);
                await stream.WriteAsync(_sequence);
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
