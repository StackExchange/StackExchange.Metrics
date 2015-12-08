namespace BosunReporter.Infrastructure
{
    internal class Payload
    {
        internal int Used { get; set; }
        internal byte[] Data { get; }
        internal int MetricsCount { get; set; }

        internal Payload(int size)
        {
            Data = new byte[size];
        }
    }
}