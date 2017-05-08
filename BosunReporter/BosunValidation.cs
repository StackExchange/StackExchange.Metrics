using System.Text.RegularExpressions;

namespace BosunReporter
{
    /// <summary>
    /// Provides helper methods to ensure metric names, tag names, and tag values are all valid OpenTSDB names.
    /// </summary>
    public static class BosunValidation
    {
        private static readonly Regex _validTsdbString = new Regex(@"^[a-zA-Z0-9\-_./]+$");

        /// <summary>
        /// A regular expression which matches any character not valid for OpenTSDB names.
        /// </summary>
        public static readonly Regex InvalidChars = new Regex(@"[^a-zA-Z0-9\-_./]+");

        /// <summary>
        /// Returns true if <paramref name="name"/> is a valid OpenTSDB metric name.
        /// </summary>
        public static bool IsValidMetricName(string name)
        {
            return _validTsdbString.IsMatch(name);
        }

        /// <summary>
        /// Returns true if <paramref name="name"/> is a valid OpenTSDB tag name.
        /// </summary>
        public static bool IsValidTagName(string name)
        {
            return _validTsdbString.IsMatch(name);
        }

        /// <summary>
        /// Returns true if <paramref name="value"/> is a valid OpenTSDB tag value.
        /// </summary>
        public static bool IsValidTagValue(string value)
        {
            return _validTsdbString.IsMatch(value);
        }
    }
}
