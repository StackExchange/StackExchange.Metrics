using System.Text.RegularExpressions;

namespace BosunReporter
{
    internal static class Validation
    {
        private static readonly Regex _validTsdbString = new Regex(@"^[a-zA-Z0-9\-_./]+$");
        public static readonly Regex InvalidChars = new Regex(@"[^a-zA-Z0-9\-_./]+");

        public static bool IsValidMetricName(string name)
        {
            return _validTsdbString.IsMatch(name);
        }

        public static bool IsValidTagName(string name)
        {
            return _validTsdbString.IsMatch(name);
        }

        public static bool IsValidTagValue(string value)
        {
            return _validTsdbString.IsMatch(value);
        }
    }
}
