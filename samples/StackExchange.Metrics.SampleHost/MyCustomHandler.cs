using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.SampleHost
{
    /// <summary>
    /// A simple handler that serializes metrics as plain text in the format:
    /// &lt;metric_name&gt;,&lt;metric_value&gt;\n
    /// </summary>
    public class MyCustomHandler : BufferedHttpMetricHandler
    {
        private static readonly MediaTypeHeaderValue _plainText = new MediaTypeHeaderValue("text/plain");

        /// <summary>
        /// Constructs a new handler pointing at the specified <see cref="System.Uri" />.
        /// </summary>
        /// <param name="uri">
        /// <see cref="System.Uri" /> of the HTTP endpoint.
        /// </param>
        public MyCustomHandler(Uri uri)
        {
            Uri = uri;
        }

        /// <summary>
        /// Gets or sets the URI used by the handler.
        /// </summary>
        public Uri Uri { get; }

        // no pre or post-amble for this handler
        protected override int GetPostambleLength(PayloadType payloadType) => 0;
        protected override int GetPreambleLength(PayloadType payloadType) => 0;

        // usually used to trim things from the sequence; e.g. trailing commas or line breaks
        protected override void PrepareSequence(ref ReadOnlySequence<byte> sequence, PayloadType payloadType)
        {
        }

        protected override HttpClient CreateHttpClient()
        {
            var httpClient = base.CreateHttpClient();
            // TODO: sane auth implementation
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "...");
            return httpClient;
        }

        protected override ValueTask SendCounterAsync(ReadOnlySequence<byte> sequence) => SendAsync(Uri, HttpMethod.Post, PayloadType.Counter, _plainText, sequence);
        protected override ValueTask SendCumulativeCounterAsync(ReadOnlySequence<byte> sequence) => SendAsync(Uri, HttpMethod.Post, PayloadType.CumulativeCounter, _plainText, sequence);
        protected override ValueTask SendGaugeAsync(ReadOnlySequence<byte> sequence) => SendAsync(Uri, HttpMethod.Post, PayloadType.Gauge, _plainText, sequence);
        // this implementation doesn't support metadata
        protected override ValueTask SendMetadataAsync(ReadOnlySequence<byte> sequence) => default;

        protected override void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<Metadata> metadata)
        {
            // this implementation doesn't support metadata
        }

        private const int ValueDecimals = 5;
        private static readonly byte[] s_comma = Encoding.UTF8.GetBytes(",");
        private static readonly byte[] s_newLine = Encoding.UTF8.GetBytes("\n");
        static readonly StandardFormat s_valueFormat = StandardFormat.Parse("F" + ValueDecimals);

        protected override void SerializeMetric(IBufferWriter<byte> writer, in MetricReading reading)
        {
            // first calculate how much space we need
            var encoding = Encoding.UTF8;
            var length = encoding.GetByteCount(reading.Name) + s_comma.Length;

            // calculate the length needed to render the value
            var value = reading.Value;
            var valueIsWhole = value % 1 == 0;
            int valueLength = 1; // first digit
            if (!valueIsWhole)
            {
                valueLength += 1 + ValueDecimals; // + decimal point + decimal digits
            }

            // calculate the remaining digits before the decimal point
            var valueAsLong = (long)value;
            while ((valueAsLong /= 10) > 0)
            {
                valueLength++;
            }

            length += valueLength;
            length += s_newLine.Length;

            // go grab some space in the buffer writer
            var memory = writer.GetMemory(length);
            // we also need a byte array otherwise we can't do all the encoding bits
            var arraySegment = memory.GetArray();
            var buffer = arraySegment.Array;
            var bytesWritten = arraySegment.Offset;
            // write data into the buffer
            {
                // write the name into the buffer
                bytesWritten += encoding.GetBytes(reading.Name, 0, reading.Name.Length, buffer, bytesWritten);

                // separator (,)
                CopyToBuffer(s_comma);

                // write the value as a long
                var valueBytesWritten = 0;
                if (valueIsWhole)
                {
                    if (!Utf8Formatter.TryFormat((long)reading.Value, buffer.AsSpan(bytesWritten, valueLength), out valueBytesWritten))
                    {
                        var ex = new InvalidOperationException(
                            "Span was not big enough to write metric value"
                        );

                        ex.Data.Add("Value", valueAsLong.ToString());
                        ex.Data.Add("Size", valueLength.ToString());
                        throw ex;
                    }
                }
                // write the value as a fixed point (f5) decimal
                else if (!Utf8Formatter.TryFormat(reading.Value, buffer.AsSpan(bytesWritten, valueLength), out valueBytesWritten, s_valueFormat))
                {
                    var ex = new InvalidOperationException(
                        "Span was not big enough to write metric value"
                    );

                    ex.Data.Add("Value", value.ToString("f5"));
                    ex.Data.Add("Size", valueLength.ToString());
                    throw ex;
                }

                bytesWritten += valueBytesWritten;

                // new line (\n)
                CopyToBuffer(s_newLine);

                // now write it to the buffer writer
                writer.Write(memory.Span.Slice(0, length));

                void CopyToBuffer(byte[] source)
                {
                    Array.Copy(source, 0, buffer, bytesWritten, source.Length);
                    bytesWritten += source.Length;
                }
            }
        }

        protected override Task WritePostambleAsync(Stream stream, PayloadType payloadType) => Task.CompletedTask;
        protected override Task WritePreambleAsync(Stream stream, PayloadType payloadType) => Task.CompletedTask;
    }
}
