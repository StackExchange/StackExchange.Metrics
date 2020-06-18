using System.Net.Http.Headers;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Common media types used by the <see cref="BufferedHttpMetricHandler"/>.
    /// </summary>
    public static class MediaTypes
    {
        /// <summary>
        /// Media type used for JSON payloads: application/json
        /// </summary>
        public static readonly MediaTypeHeaderValue Json = new MediaTypeHeaderValue("application/json");
    }
}
