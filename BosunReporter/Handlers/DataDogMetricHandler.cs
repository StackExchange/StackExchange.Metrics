using BosunReporter.Infrastructure;
using Pipelines.Sockets.Unofficial.Buffers;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BosunReporter.Handlers
{
    /// <summary>
    /// Implements <see cref="BufferedMetricHandler" /> by sending data to the DataDog REST API.
    /// </summary>
    public class DataDogMetricHandler : HttpJsonMetricHandler
    {
        static readonly HashSet<string> _supportedUnits = new HashSet<string>(
            new[] {
                // from https://docs.datadoghq.com/developers/metrics/#units
                // BYTES
                "bit", "byte", "kibibyte", "mebibyte", "gibibyte", "tebibyte", "pebibyte", "exbibyte",
                // TIME
                "nanosecond", "microsecond", "millisecond", "second", "minute", "hour", "day", "week",
                // PERCENTAGE
                "percent_nano", "percent", "apdex", "fraction",
                // NETWORK
                "connection", "request", "packet", "segment", "response", "message", "payload", "timeout", "datagram", "route", "session",
                // SYSTEM
                "process", "thread", "host", "node", "fault", "service", "instance", "cpu",
                // DISK
                "file", "inode", "sector", "block",
                // GENERAL
                "buffer", "error", "read", "write", "occurrence", "event", "time", "unit", "operation", "item", "task", "worker", "resource", "garbage collection", "email", "sample", "stage", "monitor", "location", "check", "attempt", "device", "update", "method", "job", "container", "execution", "throttle", "invocation", "user", "success", "build", "prediction",
                // DB
                "table", "index", "lock", "transaction", "query", "row", "key", "command", "offset", "record", "object", "cursor", "assertion", "scan", "document", "shard", "flush", "merge", "refresh", "fetch", "column", "commit", "wait", "ticket", "question",
                // CACHE
                "hit", "miss", "eviction", "get", "set",
                // MONEY
                "dollar", "cent",
                // MEMORY
                "page", "split",
                // FREQUENCY
                "hertz", "kilohertz", "megahertz", "gigahertz",
                // LOGGING
                "entry",
                //
                "degree celsius", "degree fahrenheit",
                // CPU
                "nanocore", "microcore", "millicore", "core", "kilocore", "megacore", "gigacore", "teracore", "petacore", "exacore"
            }, StringComparer.OrdinalIgnoreCase
        );

        static readonly Dictionary<string, string> _typeMappings = new Dictionary<string, string>
        {
            [MetricType.Counter.ToString()] = "count",
            // TODO: see if this is properly supported by DataDog
            [MetricType.CumulativeCounter.ToString()] = "count",
            [MetricType.Gauge.ToString()] = "gauge"
        };

        static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            Converters =
            {
                new JsonEpochConverter(),
                new JsonMetricReadingConverter()
            }
        };

        static readonly byte[] s_preamble = Encoding.UTF8.GetBytes(@"{""series"":[");
        static readonly byte[] s_postamble = Encoding.UTF8.GetBytes("]}");
        static readonly byte[] s_comma = Encoding.UTF8.GetBytes(",");
        static readonly byte[] s_metadataSentinel = Encoding.UTF8.GetBytes("__metadata__");

        readonly Uri _metricUri;
        readonly Uri _metadataUri;
        readonly Lazy<BufferWriter<byte>> _metricBufferWriter;
        readonly Lazy<BufferWriter<byte>> _metadataBufferWriter;
        List<MetadataPayload> _metadata;

        /// <summary>
        /// Constructs a new <see cref="DataDogMetricHandler" /> pointing at the specified URL.
        /// </summary>
        /// <param name="baseUrl">
        /// URL of a DataDog endpoint.
        /// </param>
        public DataDogMetricHandler(string baseUrl)
        {
            if (baseUrl == null)
            {
                throw new ArgumentNullException(baseUrl);
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new ArgumentException("Invalid URI specified", nameof(baseUrl));
            }

            _metricUri = new Uri(baseUri, "/api/v1/series");
            _metadataUri = new Uri(baseUri, "/api/v1/metrics");

            BufferWriter<byte> CreateBufferWriter() => BufferWriter<byte>.Create(MaxPayloadSize);

            _metricBufferWriter = new Lazy<BufferWriter<byte>>(CreateBufferWriter);
            _metadataBufferWriter = new Lazy<BufferWriter<byte>>(CreateBufferWriter);
        }

        /// <summary>
        /// Constructs a new <see cref="DataDogMetricHandler" /> pointing at the specified URL
        /// and using the given API and app keys.
        /// </summary>
        /// <param name="baseUrl">
        /// URL of a DataDog endpoint.
        /// </param>
        /// <param name="apiKey">
        /// An API key.
        /// </param>
        /// <param name="appKey">
        /// An app key.
        /// </param>
        public DataDogMetricHandler(string baseUrl, string apiKey, string appKey) : this(baseUrl)
        {
            _metricUri = new UriBuilder(_metricUri)
            {
                Query = $"api_key={apiKey}"
            }.Uri;

            _metadataUri = new UriBuilder(_metadataUri)
            {
                Query = $"api_key={apiKey}&application_key={appKey}"
            }.Uri;
        }

        /// <inheritdoc />
        protected override int GetPreambleLength(PayloadType payloadType) => payloadType == PayloadType.Metadata ? 0 : s_preamble.Length;

        /// <inheritdoc />
        protected override int GetPostambleLength(PayloadType payloadType) => payloadType == PayloadType.Metadata ? 0 : s_postamble.Length;

        /// <inheritdoc />
        protected override async ValueTask SendMetadataAsync(ReadOnlyMemory<byte> buffer)
        {
            // metadata needs to be posted separately, one call per metric
            // so write it into a buffer writer and continually flush it out
            using (var bufferWriter = BufferWriter<byte>.Create())
            {
                foreach (var metadata in _metadata)
                {
                    var uri = new UriBuilder(_metadataUri)
                    {
                        Path = _metadataUri.AbsolutePath + "/" + metadata.Metric
                    }.Uri;

                    using (var utfWriter = new Utf8JsonWriter(bufferWriter.Writer))
                    {
                        JsonSerializer.Serialize(utfWriter, metadata, s_jsonOptions);
                    }

                    using (var metadataBuffer = bufferWriter.Flush())
                    {
                        var sequence = metadataBuffer.Value;
                        if (sequence.IsSingleSegment)
                        {
                            await SendAsync(uri, HttpMethod.Put, PayloadType.Metadata, sequence.First);
                        }
                        else
                        {
                            foreach (var segment in sequence)
                            {
                                await SendAsync(uri, HttpMethod.Put, PayloadType.Metadata, segment);
                            }
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        protected override ValueTask SendCounterAsync(ReadOnlyMemory<byte> buffer) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Counter, buffer);

        /// <inheritdoc />
        protected override ValueTask SendCumulativeCounterAsync(ReadOnlyMemory<byte> buffer)
        {
            // TODO: verify that DataDog handles this gracefully
            return SendAsync(_metricUri, HttpMethod.Post, PayloadType.CumulativeCounter, buffer);
        }

        /// <inheritdoc />
        protected override ValueTask SendGaugeAsync(ReadOnlyMemory<byte> buffer) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Gauge, buffer);

        /// <inheritdoc />
        protected override void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<MetaData> metadata)
        {
            _metadata = metadata.GroupBy(x => x.Metric)
                .Select(
                    g => new MetadataPayload(
                        metric: g.Key,
                        type: g.Where(x => x.Name == MetadataNames.Rate).Select(x => _typeMappings.TryGetValue(x.Value, out var rate) ? rate : null).FirstOrDefault(),
                        description: g.Where(x => x.Name == MetadataNames.Description).Select(x => x.Value).FirstOrDefault(),
                        unit: g.Where(x => x.Name == MetadataNames.Unit && _supportedUnits.Contains(x.Value)).Select(x => x.Value).FirstOrDefault()
                    )
                )
                .ToList();

            var span = writer.GetSpan(s_metadataSentinel.Length);
            // HACK: write a sentinel value to make sure MetricsCollector flushes metrics
            writer.Write(s_metadataSentinel);
        }

        /// <inheritdoc />
        protected override BufferWriter<byte> CreateBufferWriter(PayloadType payloadType)
        {
            switch (payloadType)
            {
                case PayloadType.Counter:
                case PayloadType.CumulativeCounter:
                case PayloadType.Gauge:
                    return _metricBufferWriter.Value;
                case PayloadType.Metadata:
                    return _metadataBufferWriter.Value;
                default:
                    throw new ArgumentOutOfRangeException(nameof(payloadType));
            }
        }

        /// <inheritdoc />
        protected override void SerializeMetric(IBufferWriter<byte> writer, in MetricReading reading)
        {
            using (var utfWriter = new Utf8JsonWriter(writer))
            {
                JsonSerializer.Serialize(utfWriter, reading, s_jsonOptions);
            }

            writer.Write(s_comma);
        }

        /// <inheritdoc />
        protected override Task WritePreambleAsync(Stream stream, PayloadType payloadType)
        {
            if (payloadType == PayloadType.Metadata)
            {
                return Task.CompletedTask;
            }

            return stream.WriteAsync(s_preamble, 0, s_preamble.Length);
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
        protected override void PrepareBuffer(ref ReadOnlyMemory<byte> buffer, PayloadType payloadType)
        {
            if (payloadType == PayloadType.Metadata)
            {
                return;
            }

            // make sure that there are no trailing or leading commas
            var firstByte = buffer.Span[0];
            if (firstByte == ',')
            {
                buffer = buffer.Slice(1);
            }

            var lastIndex = buffer.Length - 1;
            var lastByte = buffer.Span[lastIndex];
            if (lastByte == ',')
            {
                buffer = buffer.Slice(0, lastIndex);
            }
        }

        private readonly struct MetadataPayload
        {
            public MetadataPayload(string metric, string type, string description, string unit)
            {
                Metric = metric;
                Type = type;
                Description = description;
                Unit = unit;
            }

            [JsonIgnore]
            public string Metric { get; }
            [JsonPropertyName("type")]
            public string Type { get; }
            [JsonPropertyName("description")]
            public string Description { get; }
            [JsonPropertyName("unit")]
            public string Unit { get; }
        }


        class JsonMetricReadingConverter : JsonConverter<MetricReading>
        {
            public override MetricReading Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotSupportedException();
            }

            static readonly JsonEncodedText s_metricProperty = JsonEncodedText.Encode("metric");
            static readonly JsonEncodedText s_pointsProperty = JsonEncodedText.Encode("points");
            static readonly JsonEncodedText s_tagsProperty = JsonEncodedText.Encode("tags");
            static readonly JsonEncodedText s_hostProperty = JsonEncodedText.Encode("host");
            static readonly JsonEncodedText s_hostValue = JsonEncodedText.Encode(Environment.MachineName.ToLower());

            public override void Write(Utf8JsonWriter writer, MetricReading reading, JsonSerializerOptions options)
            {
                var epochConverter = (JsonConverter<DateTime>)options.GetConverter(typeof(DateTime));
                // TODO: use string.Create in netstandard21
                var nameWithSuffix = !string.IsNullOrEmpty(reading.Suffix) ? reading.Name + reading.Suffix : reading.Name;

                writer.WriteStartObject(); // {
                writer.WriteString(s_metricProperty, nameWithSuffix); // "metric": "name"
                writer.WritePropertyName(s_pointsProperty); // ,"points": 
                writer.WriteStartArray(); // [
                writer.WriteStartArray(); // [
                epochConverter.Write(writer, reading.Timestamp, options); // 123456789,
                writer.WriteNumberValue(reading.Value); // 1.23
                writer.WriteEndArray(); // ]
                writer.WriteEndArray(); // ]
                if (reading.Tags.Count > 0)
                {
                    writer.WritePropertyName(s_tagsProperty); // ,"tags":
                    writer.WriteStartArray(); // [
                    foreach (var tag in reading.Tags)
                    {
                        writer.WriteStringValue(tag.Key + ":" + tag.Value); // "tag:value"
                    }
                    writer.WriteStartArray(); // ]
                }
                writer.WriteString(s_hostProperty, s_hostValue); // ,"host": "hostname"
                writer.WriteEndObject(); // }
            }
        }
    }
}
