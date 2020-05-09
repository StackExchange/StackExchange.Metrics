using System.Text.RegularExpressions;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics
{
    /// <summary>
    /// A delegate signature for validating metric names or tags. Used by the validation properties in <see cref="MetricSourceOptions"/>.
    /// </summary>
    public delegate bool ValidationDelegate(string value);

    /// <summary>
    /// Provides helper methods to ensure metric names, tag names, and tag values are all valid names.
    /// </summary>
    public static class MetricValidation
    {
        private static readonly Regex s_validMetricString = new Regex(@"^[a-zA-Z0-9\-_./]+$");

        /// <summary>
        /// A regular expression which matches any character not valid for metric names.
        /// </summary>
        public static readonly Regex InvalidChars = new Regex(@"[^a-zA-Z0-9\-_./]+");

        /// <summary>
        /// Returns true if <paramref name="name"/> is a valid metric name.
        /// </summary>
        public static bool IsValidMetricName(string name) => s_validMetricString.IsMatch(name);

        /// <summary>
        /// Returns true if <paramref name="name"/> is a valid tag name.
        /// </summary>
        public static bool IsValidTagName(string name) => s_validMetricString.IsMatch(name);

        /// <summary>
        /// Returns true if <paramref name="value"/> is a valid tag value.
        /// </summary>
        public static bool IsValidTagValue(string value) => s_validMetricString.IsMatch(value);
    }
}
