using System;
using System.Net;
using BosunReporter.Infrastructure;

namespace BosunReporter
{
    public class BosunPostException : Exception
    {
        internal BosunPostException(HttpStatusCode statusCode, string responseBody, Exception innerException) 
            : base("Posting to the Bosun API failed with status code " + statusCode, innerException)
        {
            Data["ResponseBody"] = responseBody;
        }

        internal BosunPostException(Exception innerException)
            : base("Posting to the Bosun API failed. Bosun did not respond.", innerException)
        {
        }
    }

    public class BosunQueueFullException : Exception
    {
        public int MetricsCount { get; }
        public int Bytes { get; }

        internal BosunQueueFullException(QueueType queueType, int metricsCount, int bytes)
            : base($"Bosun {queueType} metric queue is full. Metric data is likely being lost due to repeated failures in posting to the Bosun API.")
        {
            MetricsCount = metricsCount;
            Bytes = bytes;

            Data["MetricsCount"] = metricsCount;
            Data["Bytes"] = bytes;
        }
    }
}
