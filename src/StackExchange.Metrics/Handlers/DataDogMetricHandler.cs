using Pipelines.Sockets.Unofficial.Buffers;
using StackExchange.Metrics.Infrastructure;
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

namespace StackExchange.Metrics.Handlers
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
            [nameof(MetricType.Counter).ToLower()] = "rate",
            // TODO: see if this is properly supported by DataDog
            [nameof(MetricType.CumulativeCounter).ToLower()] = "count",
            [nameof(MetricType.Gauge).ToLower()] = "gauge"
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

        Uri _baseUri;
        Uri _metricUri;
        Uri _metadataUri;
        string _apiKey;
        string _appKey;
        PayloadTypeMetadata _metricMetadata;
        PayloadTypeMetadata _metadataMetadata;
        List<MetadataPayload> _metadata;

        /// <summary>
        /// Constructs a new <see cref="DataDogMetricHandler" /> pointing at the specified <see cref="Uri" />.
        /// </summary>
        /// <param name="baseUri">
        /// <see cref="Uri" /> of a DataDog endpoint.
        /// </param>
        public DataDogMetricHandler(Uri baseUri)
        {
            _baseUri = baseUri;

            ReconfigureUris();
        }
    
        /// <summary>
        /// Constructs a new <see cref="DataDogMetricHandler" /> pointing at the specified URL
        /// and using the given API and app keys.
        /// </summary>
        /// <param name="baseUri">
        /// URL of a DataDog endpoint.
        /// </param>
        /// <param name="apiKey">
        /// An API key.
        /// </param>
        /// <param name="appKey">
        /// An app key.
        /// </param>
        public DataDogMetricHandler(Uri baseUri, string apiKey, string appKey)
        {
            _baseUri = baseUri;
            _apiKey = apiKey;
            _appKey = appKey;

            ReconfigureUris();
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
                ReconfigureUris();
            }
        }

        /// <summary>
        /// Gets or sets an API key used by the handler.
        /// </summary>
        public string ApiKey
        {
            get => _apiKey;
            set
            {
                _apiKey = value;
                ReconfigureUris();
            }
        }

        /// <summary>
        /// Gets or sets an app key used by the handler.
        /// </summary>
        public string AppKey
        {
            get => _appKey;
            set
            {
                _appKey = value;
                ReconfigureUris();
            }
        }

        /// <inheritdoc />
        protected override int GetPreambleLength(PayloadType payloadType) => payloadType == PayloadType.Metadata ? 0 : s_preamble.Length;

        /// <inheritdoc />
        protected override int GetPostambleLength(PayloadType payloadType) => payloadType == PayloadType.Metadata ? 0 : s_postamble.Length;

        /// <inheritdoc />
        protected override async ValueTask SendMetadataAsync(ReadOnlySequence<byte> buffer)
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
                        await SendAsync(uri, HttpMethod.Put, PayloadType.Metadata, metadataBuffer.Value);
                    }
                }
            }
        }

        /// <inheritdoc />
        protected override ValueTask SendCounterAsync(ReadOnlySequence<byte> sequence) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Counter, sequence);

        /// <inheritdoc />
        protected override ValueTask SendCumulativeCounterAsync(ReadOnlySequence<byte> sequence)
        {
            // TODO: verify that DataDog handles this gracefully
            return SendAsync(_metricUri, HttpMethod.Post, PayloadType.CumulativeCounter, sequence);
        }

        /// <inheritdoc />
        protected override ValueTask SendGaugeAsync(ReadOnlySequence<byte> sequence) => SendAsync(_metricUri, HttpMethod.Post, PayloadType.Gauge, sequence);

        /// <inheritdoc />
        protected override void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<MetaData> metadata)
        {
            if (_metadataUri == null)
            {
                // no endpoint to write to, don't bother
                return;
            }

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
        protected override PayloadTypeMetadata CreatePayloadTypeMetadata(PayloadType payloadType)
        {
            PayloadTypeMetadata CreatePayloadTypeMetadata() => new PayloadTypeMetadata(BufferWriter<byte>.Create(blockSize: MaxPayloadSize));

            switch (payloadType)
            {
                case PayloadType.CumulativeCounter:
                case PayloadType.Counter:
                case PayloadType.Gauge:
                    return _metricMetadata ?? (_metricMetadata = CreatePayloadTypeMetadata());
                case PayloadType.Metadata:
                    return _metadataMetadata ?? (_metadataMetadata = CreatePayloadTypeMetadata());
                default:
                    throw new ArgumentOutOfRangeException(nameof(payloadType));
            }
        }

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
        protected override void PrepareSequence(ref ReadOnlySequence<byte> sequence, PayloadType payloadType)
        {
            if (payloadType == PayloadType.Metadata)
            {
                return;
            }

            sequence = sequence.Trim(',');
        }

        private void ReconfigureUris()
        {
            if (_baseUri != null && !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_appKey))
            {
                var basePath = _baseUri.AbsolutePath.Trim('/');
                var apiKeyQuery = "api_key=" + _apiKey;
                var appKeyQuery = "application_key=" + _appKey;

                _metricUri = new UriBuilder(_baseUri)
                {
                    Path = basePath + "/api/v1/series",
                    Query = apiKeyQuery
                }.Uri;

                _metadataUri = new UriBuilder(_baseUri)
                {
                    Path = basePath + "/api/v1/metrics",
                    Query = apiKeyQuery + "&" + appKeyQuery
                }.Uri;
            }
            else if (_baseUri != null)
            {
                _metricUri = new Uri(_baseUri, "/api/v1/series");
                _metadataUri = new Uri(_baseUri, "/api/v1/metrics");
            }
            else
            {
                _metricUri = null;
                _metadataUri = null;
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

                writer.WriteStartObject(); // {
                writer.WriteString(s_metricProperty, reading.NameWithSuffix); // "metric": "name"
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
                    writer.WriteEndArray(); // ]
                }
                writer.WriteString(s_hostProperty, s_hostValue); // ,"host": "hostname"
                writer.WriteEndObject(); // }
            }
        }
    }
}
