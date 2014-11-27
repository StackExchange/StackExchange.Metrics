using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace BosunReporter
{
    public abstract class BosunMetric
    {
        private static readonly IReadOnlyCollection<string> NO_SUFFIXES = new List<string> {""}.AsReadOnly();

        public BosunReporter BosunReporter { get; internal set; }

        public virtual IReadOnlyCollection<string> Suffixes
        {
            get { return NO_SUFFIXES; }
        }

        private string _serializedTags;
        protected internal string SerializedTags
        {
            get { return _serializedTags ?? (_serializedTags = SerializeTags()); }
        }

        private string _name;
        private readonly object _nameLock = new object();

        public string Name
        {
            get { return _name; }
            internal set
            {
                lock (_nameLock)
                {
                    if (_name != null)
                        throw new InvalidOperationException("Metrics cannot be renamed.");

                    if (value == null || !Validation.IsValidMetricName(value))
                        throw new Exception(value + " is not a valid metric name. Only characters in the regex class [a-zA-Z0-9\\-_./] are allowed.");

                    _name = value;
                }
            }
        }

        protected BosunMetric()
        {
        }

        internal IEnumerable<string> Serialize(string unixTimestamp)
        {
            if (_name == null)
                throw new NullReferenceException("Cannot serialize a metric which has not been named. Always use BosunReporter.GetMetric() to create metrics.");

            return GetSerializedMetrics(unixTimestamp);
        }

        protected abstract IEnumerable<string> GetSerializedMetrics(string unixTimestamp);

        protected string ToJson(string suffix, string value, string unixTimestamp)
        {
            return "{\"metric\":\""+ _name + suffix +"\",\"value\":"+ value +",\"tags\":"+ SerializedTags +",\"timestamp\":"+ unixTimestamp +"}";
        }

        private string SerializeTags()
        {
            var sb = new StringBuilder();
            var tags = GetTagsList();
            foreach (var tag in tags)
            {
                var value = (string)tag.FieldInfo.GetValue(this);
                if (value == null)
                {
                    if (tag.Attribute.IsOptional)
                        continue;

                    throw new InvalidOperationException(
                        String.Format(
                            "null is not a valid tag value for {0}.{1}. This tag was declared as non-optional.",
                            GetType().FullName, tag.FieldInfo.Name));
                }
                if (!Validation.IsValidTagValue(value))
                {
                    throw new InvalidOperationException(
                        String.Format(
                            "Invalid value for tag {0}.{1}. \"{2}\" is not a valid tag value. Only characters in the regex class [a-zA-Z0-9\\-_./] are allowed.",
                            GetType().FullName, tag.FieldInfo.Name, value));
                }

                // everything is already validated, so we can skip a more formal JSON parser which would handle escaping
                sb.Append(",\"" + tag.Name + "\":\"" + value + "\"");
            }

            if (sb.Length == 0)
            {
                throw new InvalidOperationException(
                    String.Format("At least one tag value must be specified for every metric. {0} was instantiated without any tag values.", GetType().FullName));
            }

            sb[0] = '{'; // replaces the first comma
            sb.Append('}');
            return sb.ToString();
        }

        private List<BosunTag> GetTagsList()
        {
            var type = GetType();
            if (BosunReporter.TagsByTypeCache.ContainsKey(type))
                return BosunReporter.TagsByTypeCache[type];

            // build list of tag members of the current type
            var fields = type.GetFields();
            var tags = new List<BosunTag>();
            foreach (var f in fields)
            {
                var metricTag = f.GetCustomAttribute<BosunTagAttribute>();
                if (metricTag != null)
                    tags.Add(new BosunTag(f, metricTag, BosunReporter.PropertyToTagName));
            }

            if (tags.Count == 0)
                throw new TypeInitializationException(type.FullName, new Exception("Type does not contain any Bosun tags. Metrics must have at least one tag to be serializable."));

            tags.Sort((a, b) => String.CompareOrdinal(a.Name, b.Name));
            BosunReporter.TagsByTypeCache[type] = tags;
            return tags;
        }
    }
}
