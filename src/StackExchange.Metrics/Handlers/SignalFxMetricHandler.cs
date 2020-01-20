using StackExchange.Metrics.Infrastructure;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StackExchange.Metrics.Handlers
{
    /// <summary>
    /// Implements <see cref="IMetricHandler"/> by sending data to SignalFx
    /// </summary>
    public class SignalFxMetricHandler : IMetricHandler
    {
        IMetricHandler _activeHandler;
        Uri _baseUri;
        string _accessToken;

        /// <summary>
        /// Constructs a new <see cref="SignalFxMetricHandler" /> pointing at the specified <see cref="Uri" />.
        /// </summary>
        /// <param name="baseUri">
        /// <see cref="Uri" /> of a SignalFx endpoint.
        /// </param>
        /// <remarks>
        /// If the URI points at a UDP endpoint then a StatsD endpoint is assumed, otherwise if the URI
        /// is HTTP or HTTPS then a REST endpoint is assumed
        /// </remarks>
        public SignalFxMetricHandler(Uri baseUri)
        {
            _baseUri = baseUri;
            _activeHandler = GetHandler();
        }

        /// <summary>
        /// Constructs a new <see cref="SignalFxMetricHandler" /> pointing at the specified <see cref="Uri" />
        /// and using the specified access token.
        /// </summary>
        /// <param name="baseUri">
        /// <see cref="Uri" /> of a SignalFx endpoint.
        /// </param>
        /// <param name="accessToken">
        /// An access token.
        /// </param>
        /// <remarks>
        /// If the URI points at a UDP endpoint then a StatsD endpoint is assumed, otherwise if the URI
        /// is HTTP or HTTPS then a REST endpoint is assumed
        /// </remarks>
        public SignalFxMetricHandler(Uri baseUri, string accessToken)
        {
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
            _baseUri = baseUri;
            _activeHandler = GetHandler();
        }

        /// <summary>
        /// Gets or sets the maximum number of payloads we can keep before we consider our buffers full.
        /// </summary>
        public long MaxPayloadCount
        {
            get
            {
                if (_activeHandler is BufferedMetricHandler bufferedHandler)
                {
                    return bufferedHandler.MaxPayloadCount;
                }
                return 0;
            }
            set
            {
                if (_activeHandler is BufferedMetricHandler bufferedHandler)
                {
                    bufferedHandler.MaxPayloadCount = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the base URI used by the handler.
        /// </summary>
        public Uri BaseUri
        {
            get => _baseUri;
            set
            {
                _baseUri = value;
                var oldHandler = _activeHandler;
                if (oldHandler != null)
                {
                    oldHandler.Dispose();
                }

                _activeHandler = GetHandler();
            }
        }

        /// <inheritdoc />
        public IMetricBatch BeginBatch() => _activeHandler.BeginBatch();

        /// <inheritdoc />
        public void Dispose() => _activeHandler.Dispose();

        /// <inheritdoc />
        public ValueTask FlushAsync(TimeSpan delayBetweenRetries, int maxRetries, Action<AfterSendInfo> afterSend, Action<Exception> exceptionHandler)
            => _activeHandler?.FlushAsync(delayBetweenRetries, maxRetries, afterSend, exceptionHandler) ?? default(ValueTask);

        /// <inheritdoc />
        public void SerializeMetadata(IEnumerable<MetaData> metadata) => _activeHandler.SerializeMetadata(metadata);

        /// <inheritdoc />
        public void SerializeMetric(in MetricReading reading) => _activeHandler.SerializeMetric(reading);

        private IMetricHandler GetHandler()
        {
            if (_baseUri == null)
            {
                return NoOpMetricHandler.Instance;
            }

            if (_baseUri.Scheme == "udp")
            {
                return new StatsdMetricHandler(_baseUri.Host, (ushort)_baseUri.Port);
            }

            if (_baseUri.Scheme == Uri.UriSchemeHttp || _baseUri.Scheme == Uri.UriSchemeHttps)
            {
                if (_accessToken != null)
                {
                    return new JsonMetricHandler(_baseUri, _accessToken);
                }
                return new JsonMetricHandler(_baseUri);
            }

            throw new ArgumentOutOfRangeException(nameof(_baseUri), $"URI scheme {_baseUri.Scheme} is not supported.");
        }

        /// <summary>
        /// Implements <see cref="HttpJsonMetricHandler" /> by sending data to the SignalFX REST API.
        /// </summary>
        private class JsonMetricHandler : HttpJsonMetricHandler
        {
            Uri _baseUri;
            Uri _metricUri;
            readonly string _accessToken;

            static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
            {
                Converters =
                {
                    new JsonEpochMillisecondsConverter(),
                    new JsonMetricReadingConverter()
                }
            };

            static readonly byte[] s_comma = Encoding.UTF8.GetBytes(",");
            static readonly byte[] s_counterPreamble = Encoding.UTF8.GetBytes(@"{""counter"":[");
            static readonly byte[] s_cumulativeCounterPreamble = Encoding.UTF8.GetBytes(@"{""cumulative_counter"":[");
            static readonly byte[] s_gaugePreamble = Encoding.UTF8.GetBytes(@"{""gauge"":[");
            static readonly byte[] s_postamble = Encoding.UTF8.GetBytes("]}");

            /// <summary>
            /// Constructs a new <see cref="JsonMetricHandler" /> pointing at the specified <see cref="Uri" />.
            /// </summary>
            /// <param name="baseUri">
            /// <see cref="Uri" /> of a SignalFx endpoint.
            /// </param>
            public JsonMetricHandler(Uri baseUri)
            {
                BaseUri = baseUri;
            }

            /// <summary>
            /// Constructs a new <see cref="JsonMetricHandler" /> pointing at the specified URL
            /// and using the given API key.
            /// </summary>
            /// <param name="baseUri">
            /// <see cref="Uri" /> of a SignalFx endpoint.
            /// </param>
            /// <param name="accessToken">
            /// An access token.
            /// </param>
            public JsonMetricHandler(Uri baseUri, string accessToken) : this(baseUri)
            {
                _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
            }

            /// <summary>
            /// Gets or sets the base URI used by the handler.
            /// </summary>
            public Uri BaseUri
            {
                get => _baseUri;
                set
                {
                    _baseUri = value;
                    _metricUri = value != null ? new Uri(value, "/v2/datapoint") : null;
                }
            }

            /// <inheritdoc />
            protected override HttpClient CreateHttpClient()
            {
                var httpClient = base.CreateHttpClient();
                if (_accessToken != null)
                {
                    httpClient.DefaultRequestHeaders.Add("X-SF-TOKEN", _accessToken);
                }
                return httpClient;
            }

            /// <inheritdoc />
            protected override int GetPreambleLength(PayloadType payloadType)
            {
                switch (payloadType)
                {
                    case PayloadType.Counter:
                        return s_counterPreamble.Length;
                    case PayloadType.CumulativeCounter:
                        return s_cumulativeCounterPreamble.Length;
                    case PayloadType.Gauge:
                        return s_gaugePreamble.Length;
                    default:
                        return 0;
                }
            }

            /// <inheritdoc />
            protected override int GetPostambleLength(PayloadType payloadType) => payloadType == PayloadType.Metadata ? 0 : s_postamble.Length;

            /// <inheritdoc />
            protected override void SerializeMetric(IBufferWriter<byte> writer, in MetricReading reading)
            {
                if (_metricUri == null)
                {
                    // no endpoint to write to, don't bother
                    return;
                }

                writer.Write(s_comma);

                using (var utfWriter = new Utf8JsonWriter(writer))
                {
                    JsonSerializer.Serialize(utfWriter, reading, s_jsonOptions);
                }
            }

            /// <inheritdoc />
            protected override void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<MetaData> metadata)
            {
                // this particular implementation doesn't understand metadata
            }

            /// <inheritdoc />
            protected override ValueTask SendCounterAsync(ReadOnlySequence<byte> sequence) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Counter, sequence);

            /// <inheritdoc />
            protected override ValueTask SendCumulativeCounterAsync(ReadOnlySequence<byte> sequence) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.CumulativeCounter, sequence);

            /// <inheritdoc />
            protected override ValueTask SendGaugeAsync(ReadOnlySequence<byte> sequence) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Gauge, sequence);

            /// <inheritdoc />
            protected override ValueTask SendMetadataAsync(ReadOnlySequence<byte> sequence)
            {
                // this particular implementation doesn't understand metadata
                return default;
            }

            /// <inheritdoc />
            protected override Task WritePreambleAsync(Stream stream, PayloadType payloadType)
            {
                if (payloadType == PayloadType.Metadata)
                {
                    return Task.CompletedTask;
                }

                // SignalFX needs a different preamble depending upon the
                // type of metric we're writing...
                var preamble = Array.Empty<byte>();
                switch (payloadType)
                {
                    case PayloadType.Counter:
                        preamble = s_counterPreamble;
                        break;
                    case PayloadType.CumulativeCounter:
                        preamble = s_cumulativeCounterPreamble;
                        break;
                    case PayloadType.Gauge:
                        preamble = s_gaugePreamble;
                        break;
                }

                return stream.WriteAsync(preamble, 0, preamble.Length);
            }

            /// <inheritdoc />
            protected override Task WritePostambleAsync(Stream stream, PayloadType payloadType)
            {
                if (payloadType == PayloadType.Metadata)
                {
                    return Task.CompletedTask;
                }

                return stream.WriteAsync(s_postamble, 0, s_postamble.Length);
            }

            /// <inheritdoc />
            protected override void PrepareSequence(ref ReadOnlySequence<byte> sequence, PayloadType payloadType)
            {
                if (payloadType == PayloadType.Metadata)
                {
                    return;
                }

                sequence = sequence.Trim(',');
            }

            class JsonMetricReadingConverter : JsonConverter<MetricReading>
            {
                public override MetricReading Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                {
                    throw new NotSupportedException();
                }

                static readonly JsonEncodedText s_metricProperty = JsonEncodedText.Encode("metric");
                static readonly JsonEncodedText s_valueProperty = JsonEncodedText.Encode("value");
                static readonly JsonEncodedText s_dimensionsProperty = JsonEncodedText.Encode("dimensions");
                static readonly JsonEncodedText s_timestampProperty = JsonEncodedText.Encode("timestamp");

                public override void Write(Utf8JsonWriter writer, MetricReading reading, JsonSerializerOptions options)
                {
                    var epochConverter = (JsonConverter<DateTime>)options.GetConverter(typeof(DateTime));

                    writer.WriteStartObject(); // {
                    writer.WriteString(s_metricProperty, reading.NameWithSuffix); // "metric": "name"
                    writer.WriteNumber(s_valueProperty, reading.Value); // ,"value": 1.23
                    if (reading.Tags.Count > 0)
                    {
                        writer.WritePropertyName(s_dimensionsProperty); // ,"dimensions":
                        writer.WriteStartObject(); // {
                        foreach (var tag in reading.Tags)
                        {
                            writer.WriteString(tag.Key, tag.Value); // ,"tag": "value"
                        }
                        writer.WriteEndObject(); // }
                    }
                    writer.WritePropertyName(s_timestampProperty);
                    epochConverter.Write(writer, reading.Timestamp, options); // ,"timestamp": 1234567
                    writer.WriteEndObject(); // }
                }
            }

            class JsonEpochMillisecondsConverter : JsonConverter<DateTime>
            {
                static readonly DateTime s_epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                {
                    return s_epoch.AddMilliseconds(reader.GetInt64());
                }

                public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
                {
                    writer.WriteNumberValue((long)(value - s_epoch).TotalMilliseconds);
                }
            }
        }
    }
}
