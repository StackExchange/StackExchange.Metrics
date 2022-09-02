using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Buffers;
using StackExchange.Metrics.Infrastructure;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Metrics.Handlers
{
    /// <summary>
    /// Implements <see cref="IMetricHandler" /> by sending data to a statsd endpoint.
    /// </summary>
    public class StatsdMetricHandler : IMetricHandler
    {
        string _host;
        ushort _port;
        IMetricHandler _activeHandler;

        /// <summary>
        /// Constructs a new <see cref="StatsdMetricHandler" /> pointing at the specified host and port.
        /// </summary>
        /// <param name="host">
        /// Host of a statsd endpoint.
        /// </param>
        /// <param name="port">
        /// Port of a stats endpoint.
        /// </param>
        /// <remarks>
        /// If the host or port are set to <c>null</c>/<c>0</c> this handler will be deactivated (every operation is a no-op).
        /// Otherwise it sends data to a StatsD endpoint via UDP.
        /// </remarks>
        public StatsdMetricHandler(string host, ushort port)
        {
            _host = host;
            _port = port;
            _activeHandler = GetHandler();
        }

        /// <summary>
        /// Gets or sets the maximum number of payloads we can keep before we consider our buffers full.
        /// </summary>
        public long MaxPayloadCount
        {
            get
            {
                if (_activeHandler is BufferedStatsdMetricHandler bufferedHandler)
                {
                    return bufferedHandler.MaxPayloadCount;
                }
                return 0;
            }
            set
            {
                if (_activeHandler is BufferedStatsdMetricHandler bufferedHandler)
                {
                    bufferedHandler.MaxPayloadCount = value;
                }
            }
        }

        /// <summary>
        /// Host to which we should send metrics to.
        /// </summary>
        public string Host
        {
            get => _host;
            set
            {
                _host = value;
                var oldHandler = _activeHandler;
                if (oldHandler != null)
                {
                    oldHandler.Dispose();
                }

                _activeHandler = GetHandler();
            }
        }

        /// <summary>
        /// Port to which we should send metrics to.
        /// </summary>
        public ushort Port
        {
            get => _port;
            set
            {
                _port = value;
                var oldHandler = _activeHandler;
                if (oldHandler != null)
                {
                    oldHandler.Dispose();
                }

                _activeHandler = GetHandler();
            }
        }

        /// <inheritdoc />
        public IMetricReadingBatch BeginBatch() => _activeHandler.BeginBatch();

        /// <inheritdoc />
        public void SerializeMetadata(IEnumerable<Metadata> metadata) => _activeHandler.SerializeMetadata(metadata);

        /// <inheritdoc />
        public void SerializeMetric(in MetricReading reading) => _activeHandler.SerializeMetric(reading);

        /// <inheritdoc />
        public ValueTask FlushAsync(TimeSpan delayBetweenRetries, int maxRetries, Action<AfterSendInfo> afterSend,
            Action<Exception> exceptionHandler) =>
            _activeHandler?.FlushAsync(delayBetweenRetries, maxRetries, afterSend, exceptionHandler) ?? default(ValueTask);

        /// <inheritdoc />
        public void Dispose() => _activeHandler.Dispose();

        private IMetricHandler GetHandler()
        {
            if (_host == null || _port == 0)
            {
                return NoOpMetricHandler.Instance;
            }

            return new BufferedStatsdMetricHandler(_host, _port);
        }
    }

    /// <summary>
    /// Implements <see cref="BufferedMetricHandler" /> by sending data to a statsd endpoint.
    /// </summary>
    internal class BufferedStatsdMetricHandler : BufferedMetricHandler
    {
        const int ValueDecimals = 5;
        static readonly byte[] s_counter = Encoding.UTF8.GetBytes("c");
        static readonly byte[] s_gauge = Encoding.UTF8.GetBytes("g");
        static readonly byte[] s_pipe = Encoding.UTF8.GetBytes("|");
        static readonly byte[] s_colon = Encoding.UTF8.GetBytes(":");
        static readonly byte[] s_newLine = Encoding.UTF8.GetBytes("\n");
        static readonly byte[] s_hash = Encoding.UTF8.GetBytes("#");
        static readonly byte[] s_comma = Encoding.UTF8.GetBytes(",");
        static readonly StandardFormat s_valueFormat = StandardFormat.Parse("F" + ValueDecimals);

        string _host;
        ushort _port;
        ValueTask<ClientSocketData> _clientSocketDataTask;
        PayloadTypeMetadata _metricMetadata;
        PayloadTypeMetadata _metadataMetadata;

        internal BufferedStatsdMetricHandler(string host, ushort port)
        {
            _host = host;
            _port = port;
            _clientSocketDataTask = CreateClientSocketDataAsync();
            // make sure our statsd data doesn't tear over packets
            MaxPayloadSize = 1400;
        }

        /// <summary>
        /// Host to which we should send metrics to.
        /// </summary>
        public string Host
        {
            get => _host;
            set
            {
                _host = value;
                _clientSocketDataTask = CreateClientSocketDataAsync();
            }
        }

        /// <summary>
        /// Port to which we should send metrics to.
        /// </summary>
        public ushort Port
        {
            get => _port;
            set
            {
                _port = value;
                _clientSocketDataTask = CreateClientSocketDataAsync();
            }
        }

        /// <inheritdoc />
        protected override void PrepareSequence(ref ReadOnlySequence<byte> sequence, PayloadType payloadType)
        {
            sequence = sequence.Trim('\n');
        }

        /// <inheritdoc />
        protected override ValueTask SendAsync(PayloadType payloadType, ReadOnlySequence<byte> sequence)
        {
            if (payloadType == PayloadType.Metadata)
            {
                // statsd implementations don't know how to handle metadata!
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
        protected override void SerializeMetadata(IBufferWriter<byte> writer, IEnumerable<Metadata> metadata)
        {
            // this particular implementation doesn't understand metadata
        }

        /// <inheritdoc />
        protected override void SerializeMetric(IBufferWriter<byte> writer, in MetricReading reading)
        {
            if (_host == null || _port == 0)
            {
                // no endpoint to write to, don't bother
                return;
            }

            // UTF-8 formatted as follows:
            // {metric}:{value}|{unit}|#{tag},{tag}

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
            var length = encoding.GetByteCount(reading.Name) + s_colon.Length;

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

                // separator (:)
                CopyToBuffer(s_colon);

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

        private ValueTask<ClientSocketData> CreateClientSocketDataAsync()
        {
            if (_host == null || _port == 0)
            {
                return new ValueTask<ClientSocketData>(default(ClientSocketData));
            }

            if (IPAddress.TryParse(_host, out IPAddress ip))
            {
                return new ValueTask<ClientSocketData>(
                    new ClientSocketData(new IPEndPoint(ip, _port))
                );
            }

            return FetchClientSocketDataAsync();

            async ValueTask<ClientSocketData> FetchClientSocketDataAsync()
            {
                var hostAddresses = await Dns.GetHostAddressesAsync(_host);
                var hostAddress = hostAddresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork || x.AddressFamily == AddressFamily.InterNetworkV6);
                if (hostAddress == null)
                {
                    throw new ArgumentException("Unable to find an IPv4 or IPv6 address for host", nameof(_host));
                }

                IPEndPoint endpoint;
                if (hostAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    endpoint = new IPEndPoint(hostAddress.MapToIPv6(), _port);
                }
                else
                {
                    endpoint = new IPEndPoint(hostAddress.MapToIPv4(), _port);
                }

                return new ClientSocketData(endpoint);
            }
        }

        private ValueTask SendMetricAsync(PayloadType type, in ReadOnlySequence<byte> sequence)
        {
            if (_clientSocketDataTask.IsCompleted)
            {
                return SendMetricAsync(_clientSocketDataTask.Result, sequence);
            }

            return FetchSocketArgsAndSendAsync(_clientSocketDataTask, sequence);

            async ValueTask FetchSocketArgsAndSendAsync(ValueTask<ClientSocketData> socketDataTask, ReadOnlySequence<byte> buffer)
            {
                await SendMetricAsync(await socketDataTask, buffer);
            }

            async ValueTask SendMetricAsync(ClientSocketData socketData, ReadOnlySequence<byte> buffer)
            {
                var socket = socketData.Socket;
                var socketArgs = socketData.Args;
                if (socket == null || socketArgs == null)
                {
                    return;
                }

                if (buffer.IsEmpty)
                {
                    return;
                }

                if (buffer.IsSingleSegment)
                {
                    var segment = buffer.First.GetArray();
                    if (socketArgs.BufferList != null)
                    {
                        socketArgs.BufferList = null;
                    }

                    socketArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
                }
                else
                {
                    if (socketArgs.Buffer != null)
                    {
                        socketArgs.SetBuffer(null, 0, 0);
                    }

                    socketArgs.BufferList = GetBufferList(buffer);
                }

                if (!socket.SendToAsync(socketArgs))
                {
                    socketArgs.Complete();
                }

                try
                {
                    await socketArgs;
                }
                catch (Exception ex)
                {
                    // reset the args so we can try again
                    _clientSocketDataTask = new ValueTask<ClientSocketData>(
                        new ClientSocketData(socketArgs.RemoteEndPoint)
                    );

                    // clean-up after ourselves
                    try
                    {
                        using (socketArgs)
                        using (socket)
                        {
                        }
                    }
                    catch { }

                    // HACK: this is a transient error, retry it without logging
                    if (ex is SocketException socketException && socketException.SocketErrorCode == SocketError.AddressFamilyNotSupported)
                    {
                        throw new MetricPostException(ex)
                        {
                            SkipExceptionHandler = true
                        };
                    }

                    // otherwise re-throw to notify callers we couldn't successfuly send
                    throw;
                }
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

        private readonly struct ClientSocketData
        {
            public ClientSocketData(EndPoint endpoint)
            {
                Socket = CreateSocket(endpoint);
                Args = new SocketAwaitableEventArgs
                {
                    RemoteEndPoint = endpoint
                };
            }

            public Socket Socket { get; }
            public SocketAwaitableEventArgs Args { get; }

            private static Socket CreateSocket(EndPoint endpoint)
            {
                var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                if (endpoint is IPEndPoint ipEndpoint && IPAddress.IsLoopback(ipEndpoint.Address))
                {
                    if (ipEndpoint.AddressFamily == AddressFamily.InterNetwork)
                    {
                        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    }
                    else if (ipEndpoint.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
                    }
                }
                else if (endpoint.AddressFamily == AddressFamily.InterNetwork)
                {
                    socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                }
                else if (endpoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                }

                return socket;
            }
        }
    }
}
