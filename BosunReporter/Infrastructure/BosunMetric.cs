using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace BosunReporter.Infrastructure
{
    public abstract class BosunMetric
    {
        public abstract string MetricType { get; }
        public MetricsCollector Collector { get; internal set; }
        public bool IsAttached { get; internal set; }

        private IReadOnlyCollection<string> _suffixes;
        private HashSet<string> _suffixSet;
        public IReadOnlyCollection<string> Suffixes
        {
            get
            {
                if (_suffixes == null)
                {
                    _suffixes = GetSuffixes().ToList().AsReadOnly();
                    _suffixSet = new HashSet<string>(_suffixes);
                }

                return _suffixes;
            }
        }

        private string _tagsJson;
        protected internal string TagsJson => _tagsJson ?? (_tagsJson = GetTagsJson(Collector.DefaultTags, Collector.TagValueConverter, Collector.TagsByTypeCache));

        private string _name;
        private readonly object _nameLock = new object();

        internal string MetricKey => GetMetricKey(TagsJson);

        public string Name
        {
            get { return _name; }
            internal set
            {
                lock (_nameLock)
                {
                    if (_name != null)
                        throw new InvalidOperationException("Metrics cannot be renamed.");

                    if (value == null || !BosunValidation.IsValidMetricName(value))
                        throw new Exception(value + " is not a valid metric name. Only characters in the regex class [a-zA-Z0-9\\-_./] are allowed.");

                    _name = value;
                }
            }
        }

        public virtual string Description { get; set; }
        public virtual string Unit { get; internal set; }

        public virtual bool SerializeInitialValue => MetricType == "counter";

        protected BosunMetric()
        {
        }

        internal string GetMetricKey(string tagsJson)
        {
            return _name + tagsJson;
        }

        // this method is only used when default tags are updated
        internal void SwapTagsJson(string tagsJson)
        {
            _tagsJson = tagsJson;
        }

        public virtual string GetDescription(string suffix)
        {
            return Description;
        }

        public virtual string GetUnit(string suffix)
        {
            return Unit;
        }

        public virtual IEnumerable<MetaData> GetMetaData()
        {
            foreach (var suffix in Suffixes)
            {
                var fullName = Name + suffix;

                yield return new MetaData(fullName, "rate", null, MetricType);

                var desc = GetDescription(suffix);
                if (!String.IsNullOrEmpty(desc))
                    yield return new MetaData(fullName, "desc", TagsJson, desc);

                var unit = GetUnit(suffix);
                if (!String.IsNullOrEmpty(unit))
                    yield return new MetaData(fullName, "unit", null, unit);
            }
        }

        protected virtual IEnumerable<string> GetSuffixes()
        {
            yield return "";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal IEnumerable<string> SerializeInternal(string unixTimestamp)
        {
            return Serialize(unixTimestamp);
        }

        protected abstract IEnumerable<string> Serialize(string unixTimestamp);

        protected void AssertAttached()
        {
            if (!IsAttached)
            {
                var ex = new InvalidOperationException("Attempting to record on a metric which is not attached to a MetricsCollector object.");
                try
                {
                    ex.Data["Metric"] = Name;
                    ex.Data["Tags"] = TagsJson;
                }
                finally
                {
                    throw ex;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected string ToJson(string suffix, int value, string unixTimestamp)
        {
            return ToJson(suffix, value.ToString("D"), unixTimestamp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected string ToJson(string suffix, long value, string unixTimestamp)
        {
            return ToJson(suffix, value.ToString("D"), unixTimestamp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected string ToJson(string suffix, double value, string unixTimestamp)
        {
            return ToJson(suffix, value.ToString("0.###############"), unixTimestamp);
        }

        private string ToJson(string suffix, string value, string unixTimestamp)
        {
            if (!_suffixSet.Contains(suffix))
            {
                throw new InvalidOperationException("Invalid suffix \"" + suffix + "\" on metric \"" + Name + "\". " +
                    "This is probably because you forgot to implement GetSuffixes() properly in a custom-coded metric type.");
            }

            return "{\"metric\":\""+ _name + suffix +"\",\"value\":"+ value +",\"tags\":"+ TagsJson +",\"timestamp\":"+ unixTimestamp +"}";
        }

        internal string GetTagsJson(ReadOnlyDictionary<string, string> defaultTags, TagValueConverterDelegate tagValueConverter, Dictionary<Type, List<BosunTag>> tagsByTypeCache)
        {
            var sb = new StringBuilder();
            foreach (var tag in GetTagsList(defaultTags, tagsByTypeCache))
            {
                var value = tag.IsFromDefault ? defaultTags[tag.Name] : (string)tag.FieldInfo.GetValue(this);
                if (tagValueConverter != null)
                    value = tagValueConverter(tag.Name, value);

                if (value == null)
                {
                    if (tag.IsOptional)
                        continue;

                    throw new InvalidOperationException(
                        $"null is not a valid tag value for {GetType().FullName}.{tag.FieldInfo.Name}. This tag was declared as non-optional.");
                }
                if (!BosunValidation.IsValidTagValue(value))
                {
                    throw new InvalidOperationException(
                        $"Invalid value for tag {GetType().FullName}.{tag.FieldInfo.Name}. \"{value}\" is not a valid tag value. " +
                        $"Only characters in the regex class [a-zA-Z0-9\\-_./] are allowed.");
                }

                // everything is already validated, so we can skip a more formal JSON parser which would handle escaping
                sb.Append(",\"" + tag.Name + "\":\"" + value + "\"");
            }

            if (sb.Length == 0)
            {
                throw new InvalidOperationException(
                    $"At least one tag value must be specified for every metric. {GetType().FullName} was instantiated without any tag values.");
            }

            sb[0] = '{'; // replaces the first comma
            sb.Append('}');
            return sb.ToString();
        }
        
        private List<BosunTag> GetTagsList(ReadOnlyDictionary<string, string> defaultTags, Dictionary<Type, List<BosunTag>> tagsByTypeCache)
        {
            var type = GetType();
            if (tagsByTypeCache.ContainsKey(type))
                return tagsByTypeCache[type];

            // build list of tag members of the current type
            var fields = type.GetFields();
            var tags = new List<BosunTag>();
            foreach (var f in fields)
            {
                var metricTag = f.GetCustomAttribute<BosunTagAttribute>();
                if (metricTag != null)
                    tags.Add(new BosunTag(f, metricTag, Collector.PropertyToTagName));
            }

            // get default tags
            if (type.GetCustomAttribute<IgnoreDefaultBosunTagsAttribute>(true) == null)
            {
                foreach (var name in defaultTags.Keys)
                {
                    if (tags.Any(t => t.Name == name))
                        continue;

                    tags.Add(new BosunTag(name));
                }
            }

            if (tags.Count == 0)
                throw new TypeInitializationException(type.FullName, new Exception("Type does not contain any Bosun tags. Metrics must have at least one tag to be serializable."));

            tags.Sort((a, b) => String.CompareOrdinal(a.Name, b.Name));
            Collector.TagsByTypeCache[type] = tags;
            return tags;
        }
    }
}
