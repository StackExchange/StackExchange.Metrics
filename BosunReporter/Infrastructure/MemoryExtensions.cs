using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BosunReporter.Infrastructure
{
    internal static class MemoryExtensions
    {
#pragma warning disable RCS1231 // Make parameter ref read-only.
        internal static ArraySegment<byte> GetArray(this Memory<byte> buffer) => GetArray((ReadOnlyMemory<byte>)buffer);
        internal static ArraySegment<byte> GetArray(this ReadOnlyMemory<byte> buffer)
#pragma warning restore RCS1231 // Make parameter ref read-only.
        {
            if (!MemoryMarshal.TryGetArray(buffer, out var segment))
            {
                throw new InvalidOperationException("MemoryMarshal.TryGetArray<byte> could not provide an array");
            }
            return segment;
        }

        internal static Task WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer)
        {
            var arraySegment = buffer.GetArray();
            return stream.WriteAsync(arraySegment.Array, arraySegment.Offset, arraySegment.Count);
        }
    }
}
