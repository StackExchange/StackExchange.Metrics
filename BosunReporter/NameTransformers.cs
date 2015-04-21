using System;
using System.Globalization;
using System.Linq;

namespace BosunReporter
{
    public static class NameTransformers
    {
        // http://stackoverflow.com/questions/18781027/regex-camel-case-to-underscore-ignore-first-occurrence
        public static Func<string, string> CamelToSnakeCase = (s) => string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString(CultureInfo.InvariantCulture)));
        
        public static Func<string, string> CamelToLowerSnakeCase = (s) =>
        {
            return string.Concat(s.Select((c, i) =>
            {
                if (char.IsUpper(c))
                    return i == 0 ? char.ToLowerInvariant(c).ToString(CultureInfo.InvariantCulture) : "_" + char.ToLowerInvariant(c);

                return c.ToString(CultureInfo.InvariantCulture);
            }));
        };

        public static Func<string, string> Sanitize = (s) =>
        {
            return BosunValidation.InvalidChars.Replace(s, m =>
            {
                if (m.Index == 0 || m.Index + m.Length == s.Length) // beginning and end of string
                    return "";

                return "_";
            });
        };
    }
}
