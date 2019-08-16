using BosunReporter.Infrastructure;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BosunReporter.Handlers
{
    /// <summary>
    /// Implements <see cref="BufferedMetricHandler" /> by sending data to the SignalFX REST API.
    /// </summary>
    public class SignalFxMetricHandler : HttpJsonMetricHandler
    {
        readonly Uri _metricUri;
        readonly string _accessToken;

        static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            Converters =
            {
                new JsonEpochConverter(),
                new JsonMetricReadingConverter()
            }
        };

        static readonly byte[] s_comma = Encoding.UTF8.GetBytes(",");
        static readonly byte[] s_counterPreamble = Encoding.UTF8.GetBytes(@"{""counter"":[");
        static readonly byte[] s_cumulativeCounterPreamble = Encoding.UTF8.GetBytes(@"{""cumulative_counter"":[");
        static readonly byte[] s_gaugePreamble = Encoding.UTF8.GetBytes(@"{""gauge"":[");
        static readonly byte[] s_postamble = Encoding.UTF8.GetBytes("]}");

        /// <summary>
        /// Constructs a new <see cref="SignalFxMetricHandler" /> pointing at the specified URL
        /// </summary>
        /// <param name="baseUrl">
        /// URL of a SignalFX endpoint.
        /// </param>
        public SignalFxMetricHandler(string baseUrl)
        {
            if (baseUrl == null)
            {
                throw new ArgumentNullException(baseUrl);
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new ArgumentException("Invalid URI specified", nameof(baseUrl));
            }

            _metricUri = new Uri(baseUri, "/v2/datapoint");
        }

        /// <summary>
        /// Constructs a new <see cref="SignalFxMetricHandler" /> pointing at the specified URL
        /// and using the given API key.
        /// </summary>
        /// <param name="baseUrl">
        /// URL of a SignalFX endpoint.
        /// </param>
        /// <param name="accessToken">
        /// An access token.
        /// </param>
        public SignalFxMetricHandler(string baseUrl, string accessToken) : this(baseUrl)
        {
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
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
        protected override ValueTask SendCounterAsync(ReadOnlySequence<byte> sequence) => default;//SendAsync(_metricUri, HttpMethod.Post, PayloadType.Counter, sequence);

        /// <inheritdoc />
        protected override ValueTask SendCumulativeCounterAsync(ReadOnlySequence<byte> sequence) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.CumulativeCounter, sequence);

        /// <inheritdoc />
        protected override ValueTask SendGaugeAsync(ReadOnlySequence<byte> sequence) => default;// SendAsync(_metricUri, HttpMethod.Post, PayloadType.Gauge, sequence);

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

            // make sure that there are no leading commas
            var firstByte = sequence.First.Span[0];
            if (firstByte == ',')
            {
                sequence = sequence.Slice(1);
            }
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
                // TODO: use string.Create in netstandard21
                var nameWithSuffix = !string.IsNullOrEmpty(reading.Suffix) ? reading.Name + reading.Suffix : reading.Name;

                writer.WriteStartObject(); // {
                writer.WriteString(s_metricProperty, nameWithSuffix); // "metric": "name"
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
    }
}