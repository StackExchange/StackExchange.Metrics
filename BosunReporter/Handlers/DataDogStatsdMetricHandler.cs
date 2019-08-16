using BosunReporter.Infrastructure;
using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Buffers;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace BosunReporter.Handlers
{
    /// <summary>
    /// Implements <see cref="BufferedMetricHandler" /> by sending data to a DataDog agent.
    /// </summary>
    public class DataDogStatsdMetricHandler : BufferedMetricHandler
    {
        readonly Socket _socket;
        readonly ValueTask<SocketAwaitableEventArgs> _socketEventArgsTask;

        PayloadTypeMetadata _metricMetadata;
        PayloadTypeMetadata _metadataMetadata;

        const int ValueDecimals = 5;
        static readonly byte[] s_counter = Encoding.UTF8.GetBytes("c");
        static readonly byte[] s_gauge = Encoding.UTF8.GetBytes("g");
        static readonly byte[] s_pipe = Encoding.UTF8.GetBytes("|");
        static readonly byte[] s_colon = Encoding.UTF8.GetBytes(":");
        static readonly byte[] s_newLine = Encoding.UTF8.GetBytes("\n");
        static readonly byte[] s_hash = Encoding.UTF8.GetBytes("#");
        static readonly byte[] s_comma = Encoding.UTF8.GetBytes(",");
        static readonly StandardFormat s_valueFormat = StandardFormat.Parse("F" + ValueDecimals);

        /// <summary>
        /// Constructs a new <see cref="DataDogStatsdMetricHandler" /> pointing at the specified <see cref="Uri" />.
        /// </summary>
        /// <param name="host">
        /// Host of a DataDog agent.
        /// </param>
        /// <param name="port">
        /// Port of a DataDog agent.
        /// </param>
        public DataDogStatsdMetricHandler(string host, ushort port)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            _socketEventArgsTask = CreateSocketEventArgsAsync(host, port);
            _socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        }

        /// <inheritdoc />
        protected override void PrepareSequence(ref ReadOnlySequence<byte> sequence, PayloadType payloadType)
        {
        }

        /// <inheritdoc />
        protected override ValueTask SendAsync(PayloadType payloadType, ReadOnlySequence<byte> sequence)
        {
            if (payloadType == PayloadType.Metadata)
            {
                // DataDog's statsd implementation doesn't know how to handle metadata!
                return default;
            }

            switch (payloadType)
            {
                case PayloadType.Counter:
                case PayloadType.CumulativeCounter:
                case PayloadType.Gauge:
                    return SendMetricAsync(payloadType, sequence);
                default:
                    throw new ArgumentOutOfRangeException(nameof(payloadType));
            }
        }

        /// <inheritdoc />
        protected override void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<MetaData> metadata)
        {
            // this particular implementation doesn't understand metadata
        }

        /// <inheritdoc />
        protected override void SerializeMetric(IBufferWriter<byte> writer, in MetricReading reading)
        {
            // UTF-8 formatted as follows:
            // {metric}:{value}|{unit}|{tag},{tag}

            // exit early if we don't support the metric
            byte[] unit;
            switch (reading.Type)
            {
                case MetricType.Counter:
                case MetricType.CumulativeCounter:
                    unit = s_counter;
                    break;
                case MetricType.Gauge:
                    unit = s_gauge;
                    break;
                default:
                    // not supported yet!
                    throw new NotSupportedException("Metric type '" + reading.Type + "' is not supported");
            }

            // calculate the length of buffer we need
            var encoding = Encoding.UTF8;
            var length = encoding.GetByteCount(reading.Name);
            if (!string.IsNullOrEmpty(reading.Suffix))
            {
                length += encoding.GetByteCount(reading.Suffix);
            }

            length += s_pipe.Length;

            var valueLength = 1 + 1 + ValueDecimals; // first number + decimal point + number of decimals
            var value = reading.Value;
            var valueAsInt = (int)value;
            while ((valueAsInt /= 10) > 0)
            {
                valueLength++;
            }

            length += valueLength;
            length += s_pipe.Length + unit.Length;

            if (reading.Tags.Count > 0)
            {
                length += s_pipe.Length + s_hash.Length;
                foreach (var tag in reading.Tags)
                {
                    length += encoding.GetByteCount(tag.Key) + s_colon.Length + encoding.GetByteCount(tag.Value) + s_comma.Length;
                }

                // take away the last comma
                length -= s_comma.Length;
            }

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

                // write the suffix into the buffer
                if (!string.IsNullOrEmpty(reading.Suffix))
                {
                    bytesWritten += encoding.GetBytes(reading.Suffix, 0, reading.Suffix.Length, buffer, bytesWritten);
                }

                // separator (:)
                CopyToBuffer(s_colon);

                // write the value as a fixed point (f5) decimal
                if (!Utf8Formatter.TryFormat(reading.Value, buffer.AsSpan(bytesWritten, valueLength), out var valueBytesWritten, s_valueFormat))
                {
                    // hmmm, this shouldn't happen, BUG!
                    throw new InvalidOperationException("Span was not big enough to write metric value");
                }

                bytesWritten += valueBytesWritten;

                // separator (|)
                CopyToBuffer(s_pipe);

                // write the unit
                CopyToBuffer(unit);

                if (reading.Tags.Count > 0)
                {
                    // separator (|)
                    CopyToBuffer(s_pipe);

                    // separator (#)
                    CopyToBuffer(s_hash);

                    foreach (var tag in reading.Tags)
                    {
                        bytesWritten += encoding.GetBytes(tag.Key, 0, tag.Key.Length, buffer, bytesWritten);
                        CopyToBuffer(s_colon);
                        bytesWritten += encoding.GetBytes(tag.Value, 0, tag.Value.Length, buffer, bytesWritten);
                        CopyToBuffer(s_comma);
                    }

                    // remove the last comma
                    bytesWritten--;
                }

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

        private ValueTask<SocketAwaitableEventArgs> CreateSocketEventArgsAsync(string host, ushort port)
        {
            if (IPAddress.TryParse(host, out IPAddress ip))
            {
                return new ValueTask<SocketAwaitableEventArgs>(
                    new SocketAwaitableEventArgs
                    {
                        RemoteEndPoint = new IPEndPoint(ip, port)
                    }
                );
            }

            return CreateSocketEventArgsAsync();

            async ValueTask<SocketAwaitableEventArgs> CreateSocketEventArgsAsync()
            {
                var hostAddresses = await Dns.GetHostAddressesAsync(host);
                var hostAddress = hostAddresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork || x.AddressFamily == AddressFamily.InterNetworkV6);
                if (hostAddress == null)
                {
                    throw new ArgumentException("Unable to find an IPv4 or IPv6 address for host", nameof(host));
                }

                IPEndPoint endpoint;
                if (hostAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    endpoint = new IPEndPoint(hostAddress.MapToIPv6(), port);
                }
                else
                {
                    endpoint = new IPEndPoint(hostAddress.MapToIPv4(), port);
                }

                return new SocketAwaitableEventArgs
                {
                    RemoteEndPoint = endpoint
                };
            }
        }

        private ValueTask SendMetricAsync(PayloadType type, in ReadOnlySequence<byte> sequence)
        {
            if (_socketEventArgsTask.IsCompleted)
            {
                return SendMetricAsync(_socketEventArgsTask.Result, sequence);
            }

            return FetchSocketArgsAndSendAsync(_socketEventArgsTask, sequence);

            async ValueTask FetchSocketArgsAndSendAsync(ValueTask<SocketAwaitableEventArgs> socketArgsTask, ReadOnlySequence<byte> buffer)
            {
                await SendMetricAsync(await socketArgsTask, buffer);
            }

            async ValueTask SendMetricAsync(SocketAwaitableEventArgs socketArgs, ReadOnlySequence<byte> buffer)
            {
                socketArgs.BufferList = GetBufferList(buffer);
                if (!_socket.SendToAsync(socketArgs))
                {
                    socketArgs.Complete();
                }

                await socketArgs;
            }
        }

        private static List<ArraySegment<byte>> GetBufferList(in ReadOnlySequence<byte> sequence)
        {
            if (sequence.IsSingleSegment)
            {
                return new List<ArraySegment<byte>>(1)
                {
                    sequence.First.GetArray()
                };
            }
            var list = new List<ArraySegment<byte>>();
            foreach (var b in sequence)
            {
                list.Add(b.GetArray());
            }

            return list;
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            try
            {
                using (await _socketEventArgsTask)
                using (_socket)
                {
                }
            }
            catch
            {
            }
        }
    }
}
