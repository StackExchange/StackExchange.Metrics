using System;
using System.Net;

namespace BosunReporter
{
    public class BosunPostException : Exception
    {
        public BosunPostException(HttpStatusCode statusCode, string responseBody, Exception innerException) 
            : base("Posting to the Bosun API failed with status code " + statusCode, innerException)
        {
            Data["ResponseBody"] = responseBody;
        }

        public BosunPostException(Exception innerException)
            : base("Posting to the Bosun API failed. Bosun did not respond.", innerException)
        {
        }
    }

    public class BosunQueueFullException : Exception
    {
        public BosunQueueFullException(Exception innerException = null)
            : base("Bosun metric queue is full. Metric data is likely being lost due to repeated failures in posting to the Bosun API.", innerException)
        {
        }
    }
}
