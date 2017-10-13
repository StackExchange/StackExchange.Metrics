using System;
using System.Collections.Generic;

namespace BosunReporter
{
    /// <summary>
    /// A delegate signature for globally modifying tag values. See <see cref="BosunOptions.TagValueConverter"/>.
    /// </summary>
    public delegate string TagValueConverterDelegate(string tagName, string tagValue);

    /// <summary>
    /// Defines initialization options for <see cref="MetricsCollector"/>.
    /// </summary>
    public class BosunOptions
    {
        /// <summary>
        /// If provided, all metric names will be prefixed with this value. This gives you the ability to keyspace your application. For example, you might
        /// want to use something like "app1.".
        /// </summary>
        public string MetricsNamePrefix;
        /// <summary>
        /// The url of the Bosun API. No path is required. If this is null, metrics will be discarded instead of sent to Bosun.
        /// </summary>
        public Uri BosunUrl;
        /// <summary>
        /// If the url for the Bosun API can change, provide a function which will be called before each API request. This takes precedence over the BosunUrl
        /// option. If this function returns null, the request will not be made, and the batch of metrics which would have been sent will be discarded.
        /// </summary>
        public Func<Uri> GetBosunUrl;
        /// <summary>
        /// The maximum size (in bytes) the HTTP body of a single post to the Bosun API can be.
        /// </summary>
        public int MaxPayloadSize = 8000;
        /// <summary>
        /// The maximum number of payloads which can be queued for sending before they start being dropped.
        /// </summary>
        public int MaxPendingPayloads = 240;
        /// <summary>
        /// If true, BosunReporter will generate an exception every time posting to the Bosun API fails with a server error (response code 5xx).
        /// </summary>
        public bool ThrowOnPostFail = false;
        /// <summary>
        /// If true, BosunReporter will generate an exception when the metric queue is full. This would most commonly be caused by an extended outage of the
        /// Bosun API. It is an indication that data is likely being lost.
        /// </summary>
        public bool ThrowOnQueueFull = true;
        /// <summary>
        /// The length of time between metric reports (snapshots). Defaults to 30 seconds.
        /// </summary>
        public TimeSpan ReportingInterval = TimeSpan.FromSeconds(30);
        /// <summary>
        /// The length of time after initializing a <see cref="MetricsCollector"/> when metadata will be posted for the first time. This delay allows for most
        /// metrics to be created before posting metadata. Defaults to 30 seconds.
        /// </summary>
        public TimeSpan MetadataReportingDelay = TimeSpan.FromSeconds(30);
        /// <summary>
        /// How often to report metadata to Bosun. Defaults to once per hour. Use TimeSpan.Zero to disable metadata reporting.
        /// </summary>
        public TimeSpan MetadataReportingInterval = TimeSpan.FromHours(1);
        /// <summary>
        /// Allows you to specify a function which takes a property name and returns a tag name. This may be useful if you want to convert PropertyName to
        /// property_name or similar transformations. This function does not apply to any tag names which are set manually via the BosunTag attribute.
        /// </summary>
        public Func<string, string> PropertyToTagName;
        /// <summary>
        /// Allows you to specify a function which takes a tag name and value, and returns a possibly altered value. This could be used as a global sanitizer
        /// or normalizer. It is applied to all tag values, including default tags. If the return value is not a valid OpenTSDB tag, an exception will be
        /// thrown. Null values are possible for the tagValue argument, so be sure to handle nulls appropriately.
        /// </summary>
        public TagValueConverterDelegate TagValueConverter;
        /// <summary>
        /// A list of tag names/values which will be automatically inculuded on every metric. The IgnoreDefaultTags attribute can be used on classes inheriting
        /// from BosunMetric to exclude default tags. If an inherited class has a conflicting BosunTag field, it will override the default tag value. Default
        /// tags will generally not be included in metadata.
        /// </summary>
        public Dictionary<string, string> DefaultTags;
        /// <summary>
        /// Enables sending metrics to the /api/count route on OpenTSDB relays which support external counters. External counters don't reset when applications
        /// reload, and are intended for low-volume metrics. For high-volume metrics, use normal counters.
        /// </summary>
        public bool EnableExternalCounters = true;
        /// <summary>
        /// If not null or empty, this string will be sent as the X-Access-Token header on all API requests to Bosun.
        /// </summary>
        public string AccessToken;
        /// <summary>
        /// If the access token can change, provide a function which will be called before each API request. This takes precedence over the
        /// <see cref="AccessToken"/> option. If this function returns empty or null, the X-Access-Token header will be omitted.
        /// </summary>
        public Func<string> GetAccessToken;
    }
}
