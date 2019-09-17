using System;
using System.Globalization;
using System.Linq;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Provides a set of commonly useful metric name and tag name/value converters.
    /// </summary>
    public static class NameTransformers
    {
        // http://stackoverflow.com/questions/18781027/regex-camel-case-to-underscore-ignore-first-occurrence
        /// <summary>
        /// Converts CamelCaseNames to Snake_Case_Names.
        /// </summary>
        public static string CamelToSnakeCase(string s)
        {
            return string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Converts CamelCaseNames to snake_case_names with all lowercase letters.
        /// </summary>
        public static string CamelToLowerSnakeCase(string s)
        {
            return string.Concat(s.Select((c, i) =>
            {
                if (char.IsUpper(c))
                    return i == 0 ? char.ToLowerInvariant(c).ToString(CultureInfo.InvariantCulture) : "_" + char.ToLowerInvariant(c);

                return c.ToString(CultureInfo.InvariantCulture);
            }));
        }

        /// <summary>
        /// Sanitizes a metric name or tag name/value by replacing illegal characters with an underscore.
        /// </summary>
        public static string Sanitize(string s)
        {
            return MetricValidation.InvalidChars.Replace(s, m =>
            {
                if (m.Index == 0 || m.Index + m.Length == s.Length) // beginning and end of string
                    return "";

                return "_";
            });
        }
    }
}
