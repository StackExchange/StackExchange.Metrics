using Pipelines.Sockets.Unofficial.Buffers;
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
    /// Implements <see cref="BufferedMetricHandler" /> by sending data to a Bosun endpoint.
    /// </summary>
    public class BosunMetricHandler : HttpJsonMetricHandler
    {
        static readonly byte[] s_comma;
        static readonly byte[] s_startArray;
        static readonly byte[] s_endArray;
        static readonly DateTime s_minimumTimestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static readonly DateTime s_maximumTimestamp = new DateTime(2250, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        readonly JsonSerializerOptions _jsonOptions;
        readonly Dictionary<MetricKey, double> _counterValues;

        Uri _baseUri;
        Uri _metricUri;
        Uri _counterUri;
        Uri _metadataUri;
        PayloadTypeMetadata _metricMetadata;
        PayloadTypeMetadata _slowMetricMetadata;
        PayloadTypeMetadata _metadataMetadata;

        static BosunMetricHandler()
        {
            s_comma = Encoding.UTF8.GetBytes(",");
            s_startArray = Encoding.UTF8.GetBytes("[");
            s_endArray = Encoding.UTF8.GetBytes("]");
        }

        /// <summary>
        /// Constructs a new <see cref="BosunMetricHandler" /> pointing at the specified <see cref="Uri" />.
        /// </summary>
        /// <param name="baseUri">
        /// <see cref="Uri" /> of a Bosun endpoint.
        /// </param>
        public BosunMetricHandler(Uri baseUri)
        {
            BaseUri = baseUri;

            _counterValues = new Dictionary<MetricKey, double>(MetricKeyComparer.Default);
            _jsonOptions = new JsonSerializerOptions
            {
                IgnoreNullValues = true,
                Converters =
                {
                    new JsonEpochConverter(),
                    new JsonMetricReadingConverter(_counterValues)
                }
            };
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
                if (value != null)
                {
                    _metricUri = new Uri(value, "/api/put");
                    _counterUri = new Uri(value, "/api/count");
                    _metadataUri = new Uri(value, "/api/metadata/put");
                }
                else
                {
                    _metricUri = null;
                    _counterUri = null;
                    _metadataUri = null;
                }
            }
        }

        /// <summary>
        /// Enables sending metrics to the /api/count route on OpenTSDB relays which support external counters. External counters don't reset when applications
        /// reload, and are intended for low-volume metrics. For high-volume metrics,GZp use normal counters.
        /// </summary>
        public bool EnableExternalCounters { get; set; } = true;

        /// <inheritdoc />
        protected override ValueTask SendCounterAsync(ReadOnlySequence<byte> sequence) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Counter, sequence);

        /// <inheritdoc />
        protected override ValueTask SendCumulativeCounterAsync(ReadOnlySequence<byte> sequence)
        {
            if (!EnableExternalCounters)
            {
                return default;
            }

            return SendAsync(_counterUri, HttpMethod.Post, PayloadType.CumulativeCounter, sequence);
        }

        /// <inheritdoc />
        protected override ValueTask SendGaugeAsync(ReadOnlySequence<byte> sequence) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Gauge, sequence);

        /// <inheritdoc />
        protected override ValueTask SendMetadataAsync(ReadOnlySequence<byte> sequence) => SendAsync(_metadataUri, HttpMethod.Post, PayloadType.Metadata, sequence, gzip: false);

        /// <inheritdoc />
        protected override void SerializeMetric(IBufferWriter<byte> writer, in MetricReading reading)
        {
            if (reading.Type == MetricType.CumulativeCounter)
            {
                if (!EnableExternalCounters || _slowMetricMetadata == null)
                {
                    // don't serialize cumulative counters if they're not enabled
                    // or if there's no endpoint to write to
                    return;
                }
            }
            else if (_metricUri == null)
            {
                // no endpoint to write to, don't bother
                return;
            }

            if (reading.Timestamp < s_minimumTimestamp)
                throw new Exception($"Bosun cannot serialize metrics dated before {s_minimumTimestamp}.");

            if (reading.Timestamp > s_maximumTimestamp)
                throw new Exception($"Bosun cannot serialize metrics dated after {s_maximumTimestamp}.");

            var readingToWrite = reading;
            if (reading.Type == MetricType.Counter)
            {
                // Bosun treats counters somewhat differently than other providers
                // it expects a monotonically increasing value and calculates rates, etc.
                // based upon that value. Here we store the total value and use that when
                // serializing!
                var metricKey = new MetricKey(reading.Name, reading.Tags);
                if (!_counterValues.TryGetValue(metricKey, out var value))
                {
                    value = 0d;
                }

                _counterValues[metricKey] = value + reading.Value;
            }
            else if (reading.Type == MetricType.CumulativeCounter)
            {
                // Bosun requires that cumulative counters go via tsdbrelay. This
                // enforces a constraint whereby the host tag should not be present
                // on the incoming metric. Remove it here so that it doesn't throw a 500
                if (reading.Tags.ContainsKey("host"))
                {
                    readingToWrite = new MetricReading(
                        reading.Name,
                        reading.Type,
                        reading.Value,
                        reading.Tags.Remove("host"),
                        reading.Timestamp
                    );
                }
            }

            writer.Write(s_comma);

            using (var utfWriter = new Utf8JsonWriter(writer))
            {
                JsonSerializer.Serialize(utfWriter, readingToWrite, _jsonOptions);
            }
        }

        /// <inheritdoc />
        protected override void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<Metadata> metadata)
        {
            if (_metadataUri == null)
            {
                // no endpoint to write to, don't bother
                return;
            }

            using (var utfWriter = new Utf8JsonWriter(writer))
            {
                JsonSerializer.Serialize(utfWriter, metadata, _jsonOptions);
            }
        }

        /// <inheritdoc />
        protected override int GetPreambleLength(PayloadType payloadType) => payloadType == PayloadType.Metadata ? 0 : s_startArray.Length;

        /// <inheritdoc />
        protected override int GetPostambleLength(PayloadType payloadType) => payloadType == PayloadType.Metadata ? 0 : s_endArray.Length;

        /// <inheritdoc />
        protected override Task WritePreambleAsync(Stream stream, PayloadType payloadType) => payloadType == PayloadType.Metadata ? Task.CompletedTask : stream.WriteAsync(s_startArray, 0, s_startArray.Length);

        /// <inheritdoc />
        protected override Task WritePostambleAsync(Stream stream, PayloadType payloadType) => payloadType == PayloadType.Metadata ? Task.CompletedTask : stream.WriteAsync(s_endArray, 0, s_endArray.Length);

        /// <inheritdoc />
        protected override void PrepareSequence(ref ReadOnlySequence<byte> sequence, PayloadType payloadType)
        {
            if (payloadType == PayloadType.Metadata)
            {
                return;
            }

            sequence = sequence.Trim(',');
        }

        /// <inheritdoc />
        protected override PayloadTypeMetadata CreatePayloadTypeMetadata(PayloadType payloadType)
        {
            PayloadTypeMetadata CreatePayloadTypeMetadata() => new PayloadTypeMetadata(BufferWriter<byte>.Create(blockSize: MaxPayloadSize));

            switch (payloadType)
            {
                case PayloadType.CumulativeCounter:
                    return _slowMetricMetadata ?? (_slowMetricMetadata = CreatePayloadTypeMetadata());
                case PayloadType.Counter:
                case PayloadType.Gauge:
                    return _metricMetadata ?? (_metricMetadata = CreatePayloadTypeMetadata());
                case PayloadType.Metadata:
                    return _metadataMetadata ?? (_metadataMetadata = CreatePayloadTypeMetadata());
                default:
                    throw new ArgumentOutOfRangeException(nameof(payloadType), $"Unsupport payload type: {payloadType}");
            }
        }

        class JsonMetricReadingConverter : JsonConverter<MetricReading>
        {
            readonly IReadOnlyDictionary<MetricKey, double> _counterValues;

            public JsonMetricReadingConverter(IReadOnlyDictionary<MetricKey, double> counterValues)
            {
                _counterValues = counterValues;
            }

            public override MetricReading Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotSupportedException();
            }

            static readonly JsonEncodedText s_metricProperty = JsonEncodedText.Encode("metric");
            static readonly JsonEncodedText s_valueProperty = JsonEncodedText.Encode("value");
            static readonly JsonEncodedText s_tagsProperty = JsonEncodedText.Encode("tags");
            static readonly JsonEncodedText s_timestampProperty = JsonEncodedText.Encode("timestamp");

            public override void Write(Utf8JsonWriter writer, MetricReading reading, JsonSerializerOptions options)
            {
                var epochConverter = (JsonConverter<DateTime>)options.GetConverter(typeof(DateTime));
                var metricKey = new MetricKey(reading.Name, reading.Tags);
                if (!_counterValues.TryGetValue(metricKey, out var value))
                {
                    value = reading.Value;
                }

                writer.WriteStartObject(); // {
                writer.WriteString(s_metricProperty, reading.Name); // "metric": "name"
                writer.WriteNumber(s_valueProperty, value); // ,"value": 1.23
                if (reading.Tags.Count > 0)
                {
                    writer.WritePropertyName(s_tagsProperty); // ,"tags":
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
