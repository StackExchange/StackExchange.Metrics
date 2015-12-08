using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BosunReporter.Infrastructure
{
    public class MetricWriter
    {
        private Payload _payload;
        private readonly PayloadQueue _queue;

        // Denormalized references to the payload data just to remove an extra layer of indirection.
        // It's also useful to keep _payload.Used in its original state until we finalize the payload.
        private int _used;
        private byte[] _data;

        private int _startOfWrite;
        private int _payloadsCount;
        private int _bytesWrittenByPreviousPayloads;

        private int BytesWrittenToCurrentPayload => _used - _payload.Used;
        internal int TotalBytesWritten => _bytesWrittenByPreviousPayloads + BytesWrittenToCurrentPayload;


        internal int MetricsCount { get; private set; }

        internal MetricWriter(PayloadQueue queue)
        {
            _queue = queue;
        }

        internal void EndBatch()
        {
            FinalizeAndSendPayload();
            _queue.SetBatchPayloadCount(_payloadsCount);
            _payloadsCount = 0;
        }

        internal void AddMetric(string name, string suffix, double value, string tagsJson, string unixTimestamp)
        {
            MarkStartOfWrite();

            Append("{\"metric\":\"");
            Append(name);
            if (!string.IsNullOrEmpty(suffix))
                Append(suffix);
            Append("\",\"value\":");
            Append(value);
            Append(",\"tags\":");
            Append(tagsJson);
            Append(",\"timestamp\":");
            Append(unixTimestamp);
            Append("},");

            EndOfWrite();
        }

        private void Append(string s)
        {
            var len = s.Length;
            EnsureRoomFor(len);

            var data = _data;
            var used = _used;
            for (var i = 0; i < len; i++, used++)
            {
                data[used] = (byte)s[i];
            }

            _used = used;
        }

        private void Append(double d)
        {
            Append(d.ToString("R")); // todo - use Grisu
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkStartOfWrite()
        {
            _startOfWrite = _used;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EndOfWrite()
        {
            MetricsCount++;

            if (_used + 150 >= _data.Length)
            {
                // If there aren't at least 150 bytes left in the buffer,
                // there probably isn't enough room to write another metric,
                // so we're just going to flush it now.
                FinalizeAndSendPayload();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureRoomFor(int length)
        {
            if (_data == null || _used + length > _data.Length)
            {
                SwapPayload();
                AssertLength(_data, _used + length);
            }
        }

        private void SwapPayload()
        {
            var newPayload = _queue.GetPayloadForMetricWriter();
            var newData = newPayload.Data;
            var newUsed = newPayload.Used;

            if (newUsed == 0)
            {
                // payload is fresh, so we make the first character an open bracket
                newData[0] = (byte)'[';
            }
            else
            {
                // we're reusing a previously finalized payload, so we need to turn the close bracket into a comma
                newData[_used - 1] = (byte)',';
            }

            newUsed++;

            if (_data != null)
            {
                var oldStartOfWrite = _startOfWrite;
                _startOfWrite = newUsed;

                if (oldStartOfWrite > _used)
                {
                    // We started writing a metric to the old payload, but ran out of room.
                    // Need to copy what we started to the new payload.
                    var len = _used - oldStartOfWrite;
                    AssertLength(newData, newUsed + len);
                    Array.Copy(_data, oldStartOfWrite, newData, newUsed, len);
                    newUsed += len;

                    _used = oldStartOfWrite; // don't want an incomplete metric in the old buffer
                }

                FinalizeAndSendPayload();
            }

            _payload = newPayload;
            _used = newUsed;
            _data = newData;
            MetricsCount = newPayload.MetricsCount;
        }

        private static void AssertLength(byte[] array, int length)
        {
            if (array.Length < length)
                throw new Exception($"BosunReporter is trying to write something way too big. This shouldn't happen. Are you using a crazy number of tags on a metric? Length {length}.");
        }

        private void FinalizeAndSendPayload()
        {
            if (_used > 1 && _data != null)
            {
                // need to change the last character from a comma to a close bracket
                _data[_used - 1] = (byte)']';

                var payload = _payload;
                _bytesWrittenByPreviousPayloads += BytesWrittenToCurrentPayload;

                // update the Used property of the payload
                payload.Used = _used;
                payload.MetricsCount = MetricsCount;

                _queue.AddPendingPayload(payload);
                _payloadsCount++;
            }

            _payload = null;
            _used = 0;
            _data = null;
            MetricsCount = 0;
        }
    }
}