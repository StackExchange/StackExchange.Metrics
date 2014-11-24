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
        private static readonly Dictionary<Type, List<BosunTag>> _tagsByTypeCache = new Dictionary<Type, List<BosunTag>>();

        public readonly string AggregatorTagName;
        public BosunReporter BosunReporter { get; internal set; }

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
            // ReSharper disable DoNotCallOverridableMethodsInConstructor
            AggregatorTagName = GetAggregatorTagName(); // this virtual method is safe to call in the base constructor, as implemented.
            // ReSharper restore DoNotCallOverridableMethodsInConstructor
        }

        internal IEnumerable<string> Serialize(string unixTimestamp)
        {
            if (_name == null)
                throw new NullReferenceException("Cannot serialize a metric which has not been named. Always use BosunReporter.GetMetric() to create metrics.");

            return GetSerializedMetrics(unixTimestamp);
        }

        protected abstract IEnumerable<string> GetSerializedMetrics(string unixTimestamp);

        protected string ToJson(string value, string tags, string unixTimestamp)
        {
            return "{\"metric\":\""+ _name +"\",\"value\":"+ value +",\"tags\":"+ tags +",\"timestamp\":"+ unixTimestamp +"}";
        }

        protected internal string SerializeTags(string aggregatorMode = null)
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

            if (AggregatorTagName != null)
            {
                sb.Append(",\"" + AggregatorTagName + "\":\"" + aggregatorMode + "\"");
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
            if (_tagsByTypeCache.ContainsKey(type))
                return _tagsByTypeCache[type];

            // build list of tag members of the current type
            var members = type.GetMembers();
            var tags = new List<BosunTag>();
            foreach (var m in members)
            {
                var metricTag = m.GetCustomAttribute<BosunTagAttribute>();
                if (metricTag != null)
                    tags.Add(new BosunTag(m, metricTag));
            }

            if (tags.Count == 0 && AggregatorTagName == null)
                throw new TypeInitializationException(type.FullName, new Exception("Type does not contain any Bosun tags. Metrics must have at least one tag to be serializable."));

            tags.Sort((a, b) => String.CompareOrdinal(a.Name, b.Name));
            _tagsByTypeCache[type] = tags;
            return tags;
        }

        /// <summary>
        /// Note that this virtual method is called from the base constructor. Therefore, if you override this, DO NOT access any instance fields/properties of the child class.
        /// </summary>
        /// <returns></returns>
        protected virtual string GetAggregatorTagName()
        {
            return null;
        }
    }
}
