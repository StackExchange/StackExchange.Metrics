using System;
using System.Net;
using BosunReporter.Infrastructure;

namespace BosunReporter
{
    /// <summary>
    /// Exception uses when posting to the Bosun API fails.
    /// </summary>
    public class BosunPostException : Exception
    {
        /// <summary>
        /// The status code returned by Bosun.
        /// </summary>
        public HttpStatusCode? StatusCode { get; }

        internal BosunPostException(HttpStatusCode statusCode, string responseBody, Exception innerException)
            : base("Posting to the Bosun API failed with status code " + statusCode, innerException)
        {
            Data["ResponseBody"] = responseBody;
            StatusCode = statusCode;
        }

        internal BosunPostException(Exception innerException)
            : base("Posting to the Bosun API failed. Bosun did not respond.", innerException)
        {
        }
    }

    /// <summary>
    /// Exception used when a BosunReporter queue is full and payloads are being dropped. This typically happens after repeated failures to post to the API.
    /// </summary>
    public class BosunQueueFullException : Exception
    {
        /// <summary>
        /// The number of data points which were lost.
        /// </summary>
        public int MetricsCount { get; }
        /// <summary>
        /// The number of bytes in the dropped payload.
        /// </summary>
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
