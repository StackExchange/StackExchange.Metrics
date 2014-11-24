using System;

namespace BosunReporter
{
    public class BosunReporterOptions
    {
        /// <summary>
        /// If provided, all metric names will be prefixed with this value. This gives you the ability to keyspace your application.
        /// For example, you might want to use something like "app1.".
        /// </summary>
        public string MetricsNamePrefix;
        /// <summary>
        /// The url of the Bosun API. No path is required. If this is null, metrics will be discarded instead of sent to Bosun.
        /// </summary>
        public Uri BosunUrl;
        /// <summary>
        /// If the url for the Bosun API can change, provide a function which will be called before each API request. This takes precedence over the BosunUrl option.
        /// If this function returns null, the request will not be made, and the batch of metrics which would have been sent will be discarded.
        /// </summary>
        public Func<Uri> GetBosunUrl;
        /// <summary>
        /// The maximum number of metrics which will be queued for sending.
        /// </summary>
        public int MaxQueueLength = 100000;
        /// <summary>
        /// The number of metrics which will be sent in a single post to the Bosun API .
        /// </summary>
        public int BatchSize = 250;
        /// <summary>
        /// If true, BosunReporter will throw an exception every time posting to the Bosun API fails.
        /// </summary>
        public bool ThrowOnPostFail = false;
        /// <summary>
        /// If true, BosunReporter will throw an exception when the metric queue is full.
        /// This would most commonly be caused by an extended outage of the Bosun API.
        /// It is an indication that data is likely being lost.
        /// </summary>
        public bool ThrowOnQueueFull = true;
    }
}
