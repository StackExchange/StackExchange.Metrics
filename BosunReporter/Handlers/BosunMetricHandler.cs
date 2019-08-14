using BosunReporter.Infrastructure;
using Pipelines.Sockets.Unofficial.Buffers;
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
    /// Implements <see cref="BufferedMetricHandler" /> by sending data to a Bosun endpoint.
    /// </summary>
    public class BosunMetricHandler : HttpJsonMetricHandler
    {
        static readonly byte[] s_comma;
        static readonly byte[] s_startArray;
        static readonly byte[] s_endArray;
        static readonly DateTime s_minimumTimestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static readonly DateTime s_maximumTimestamp = new DateTime(2250, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            IgnoreNullValues = true,
            Converters =
            {
                new JsonEpochConverter(),
                new JsonMetricReadingConverter()
            }
        };

        readonly Uri _metricUri;
        readonly Uri _counterUri;
        readonly Uri _metadataUri;

        BufferWriter<byte> _metricBufferWriter;
        BufferWriter<byte> _slowMetricBufferWriter;
        BufferWriter<byte> _metadataBufferWriter;

        static BosunMetricHandler()
        {
            s_comma = Encoding.UTF8.GetBytes(",");
            s_startArray = Encoding.UTF8.GetBytes("[");
            s_endArray = Encoding.UTF8.GetBytes("]");
        }

        /// <summary>
        /// Constructs a new <see cref="BosunMetricHandler" /> pointing at the specified <see cref="Uri" />.
        /// </summary>
        /// <param name="url">
        /// URL to a Bosun endpoint.
        /// </param>
        public BosunMetricHandler(string url)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("Invalid URI specified", nameof(url));
            }

            _metricUri = new Uri(uri, "/api/put");
            _counterUri = new Uri(uri, "/api/count");
            _metadataUri = new Uri(uri, "/api/metadata/put");
        }

        /// <summary>
        /// Enables sending metrics to the /api/count route on OpenTSDB relays which support external counters. External counters don't reset when applications
        /// reload, and are intended for low-volume metrics. For high-volume metrics, use normal counters.
        /// </summary>
        public bool EnableExternalCounters { get; set; } = true;

        /// <inheritdoc />
        protected override ValueTask SendCounterAsync(ReadOnlyMemory<byte> buffer) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Counter, buffer);

        /// <inheritdoc />
        protected override ValueTask SendCumulativeCounterAsync(ReadOnlyMemory<byte> buffer)
        {
            if (!EnableExternalCounters)
            {
                return default;
            }

            return SendAsync(_counterUri, HttpMethod.Post, PayloadType.CumulativeCounter, buffer);
        }

        /// <inheritdoc />
        protected override ValueTask SendGaugeAsync(ReadOnlyMemory<byte> buffer) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Gauge, buffer);

        /// <inheritdoc />
        protected override ValueTask SendMetadataAsync(ReadOnlyMemory<byte> buffer) => SendAsync(_metadataUri, HttpMethod.Post, PayloadType.Metadata, buffer);

        /// <inheritdoc />
        protected override void SerializeMetric(IBufferWriter<byte> writer, in MetricReading reading)
        {
            if (reading.Type == MetricType.CumulativeCounter && !EnableExternalCounters)
            {
                // don't serialize cumulative counters if they're not enabled
                return;
            }

            if (reading.Timestamp < s_minimumTimestamp)
                throw new Exception($"Bosun cannot serialize metrics dated before {s_minimumTimestamp}.");

            if (reading.Timestamp > s_maximumTimestamp)
                throw new Exception($"Bosun cannot serialize metrics dated after {s_maximumTimestamp}.");

            using (var utfWriter = new Utf8JsonWriter(writer))
            {
                JsonSerializer.Serialize(utfWriter, reading, s_jsonOptions);
            }

            writer.Write(s_comma);
        }

        /// <inheritdoc />
        protected override void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<MetaData> metadata)
        {
            using (var utfWriter = new Utf8JsonWriter(writer))
            {
                JsonSerializer.Serialize(utfWriter, metadata, s_jsonOptions);
            }
        }

        /// <inheritdoc />
        protected override int GetPreambleLength(PayloadType payloadType) => s_startArray.Length;

        /// <inheritdoc />
        protected override int GetPostambleLength(PayloadType payloadType) => s_endArray.Length;

        /// <inheritdoc />
        protected override Task WritePreambleAsync(Stream stream, PayloadType payloadType) => stream.WriteAsync(s_startArray, 0, s_startArray.Length);

        /// <inheritdoc />
        protected override Task WritePostambleAsync(Stream stream, PayloadType payloadType) => stream.WriteAsync(s_endArray, 0, s_endArray.Length);

        /// <inheritdoc />
        protected override void PrepareBuffer(ref ReadOnlyMemory<byte> buffer, PayloadType payloadType)
        {
            // make sure that there are no trailing or leading commas or brackets
            // we write the brackets when we send to the endpoint
            var firstByte = buffer.Span[0];
            if (firstByte == ',' || firstByte == '[')
            {
                buffer = buffer.Slice(1);
            }

            var lastIndex = buffer.Length - 1;
            var lastByte = buffer.Span[lastIndex];
            if (lastByte == ',' || lastByte == ']')
            {
                buffer = buffer.Slice(0, lastIndex);
            }
        }

        /// <inheritdoc />
        protected override BufferWriter<byte> CreateBufferWriter(PayloadType payloadType)
        {
            BufferWriter<byte> CreateBufferWriter() => BufferWriter<byte>.Create(blockSize: MaxPayloadSize);

            switch (payloadType)
            {
                case PayloadType.CumulativeCounter:
                    return _slowMetricBufferWriter ?? (_slowMetricBufferWriter = CreateBufferWriter());
                case PayloadType.Counter:
                case PayloadType.Gauge:
                    return _metricBufferWriter ?? (_metricBufferWriter = CreateBufferWriter());
                case PayloadType.Metadata:
                    return _metadataBufferWriter ?? (_metadataBufferWriter = CreateBufferWriter());
                default:
                    throw new ArgumentOutOfRangeException(nameof(payloadType));
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
            static readonly JsonEncodedText s_tagsProperty = JsonEncodedText.Encode("tags");
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