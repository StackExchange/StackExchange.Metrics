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
        private struct MetricTypeInfo
        {
            public bool NeedsPreSerialize;
            public bool IsExternalCounter;
        }

        private static readonly Dictionary<Type, MetricTypeInfo> _typeInfoCache = new Dictionary<Type, MetricTypeInfo>();
        private static readonly object _typeInfoLock = new object();

        private static readonly string[] s_singleEmptyStringArray = {""};

        public abstract string MetricType { get; }
        public MetricsCollector Collector { get; internal set; }
        public bool IsAttached { get; internal set; }

        private HashSet<string> _suffixSet;
        public IReadOnlyCollection<string> Suffixes => Array.AsReadOnly(SuffixesArray);
        internal string[] SuffixesArray { get; private set; }
        internal int SuffixCount => SuffixesArray.Length;

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

        public virtual string GetDescription(int suffixIndex)
        {
            return Description;
        }

        public virtual string GetUnit(int suffixIndex)
        {
            return Unit;
        }

        public virtual IEnumerable<MetaData> GetMetaData()
        {
            for (var i = 0; i < SuffixesArray.Length; i++)
            {
                var fullName = Name + SuffixesArray[i];

                yield return new MetaData(fullName, "rate", null, MetricType);

                var desc = GetDescription(i);
                if (!String.IsNullOrEmpty(desc))
                    yield return new MetaData(fullName, "desc", TagsJson, desc);

                var unit = GetUnit(i);
                if (!String.IsNullOrEmpty(unit))
                    yield return new MetaData(fullName, "unit", null, unit);
            }
        }

        protected virtual string[] GetImmutableSuffixesArray()
        {
            return s_singleEmptyStringArray;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SerializeInternal(MetricWriter writer, string unixTimestamp)
        {
            Serialize(writer, unixTimestamp);
        }

        protected abstract void Serialize(MetricWriter writer, string unixTimestamp);

        internal bool NeedsPreSerializeCalled()
        {
            return GetMetricTypeInfo().NeedsPreSerialize;
        }

        internal bool IsExternalCounter()
        {
            return GetMetricTypeInfo().IsExternalCounter;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void PreSerializeInternal()
        {
            PreSerialize();
        }

        protected virtual void PreSerialize()
        {
            // If this method is overriden, it will be called shortly before Serialize.
            // Unlike Serialize, which is called on all metrics in serial, PreSerialize
            // is called in parallel, which makes it better place to do computationally
            // expensive operations.
        }

        internal void LoadSuffixes()
        {
            SuffixesArray = GetImmutableSuffixesArray();
            _suffixSet = new HashSet<string>(SuffixesArray);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        protected void WriteValue(MetricWriter writer, double value, string unixTimestamp, int suffixIndex = 0)
        {
            writer.AddMetric(Name, SuffixesArray[suffixIndex], value, TagsJson, unixTimestamp);
        }

        internal string GetTagsJson(
            ReadOnlyDictionary<string, string> defaultTags,
            TagValueConverterDelegate tagValueConverter,
            Dictionary<Type, List<BosunTag>> tagsByTypeCache)
        {
            var sb = new StringBuilder();
            foreach (var tag in GetTagsList(defaultTags, tagsByTypeCache))
            {
                var value = tag.IsFromDefault ? defaultTags[tag.Name] : tag.FieldInfo.GetValue(this)?.ToString();
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
            var tagAttributes = GetTagAttributesData(type);
            if (tagAttributes.IncludeByDefault || tagAttributes.IncludeByTag?.Count > 0)
            {
                foreach (var name in defaultTags.Keys)
                {
                    var explicitInclude = false; // assignment isn't actually used, but the compiler complains without it
                    if (tagAttributes.IncludeByTag?.TryGetValue(name, out explicitInclude) == true)
                    {
                        if (!explicitInclude)
                            continue;
                    }
                    else
                    {
                        if (!tagAttributes.IncludeByDefault)
                            continue;
                    }

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

        private struct TagAttributesData
        {
            public bool IncludeByDefault;
            public Dictionary<string, bool> IncludeByTag;
        }

        private static TagAttributesData GetTagAttributesData(Type type)
        {
            var foundDefault = false;
            var includeByDefault = true;
            Dictionary<string, bool> includeByTag = null;

            var objType = typeof(object);

            while (true)
            {
                var exclude = type.GetCustomAttribute<ExcludeDefaultTagsAttribute>(false);
                var restore = type.GetCustomAttribute<RestoreDefaultTagsAttribute>(false);

#pragma warning disable 618 // using obsolete class IgnoreDefaultBosunTagsAttribute
                if (restore?.Tags.Length == 0)
                {
                    foundDefault = true;
                    includeByDefault = true;
                }
                else if (exclude?.Tags.Length == 0 || type.GetCustomAttribute<IgnoreDefaultBosunTagsAttribute>(false) != null)
                {
                    foundDefault = true;
                    includeByDefault = false;
                }
#pragma warning restore 618

                if (restore?.Tags.Length > 0)
                {
                    if (includeByTag == null)
                        includeByTag = new Dictionary<string, bool>();

                    foreach (var tag in restore.Tags)
                    {
                        includeByTag[tag] = true;
                    }
                }

                if (exclude?.Tags.Length > 0)
                {
                    if (includeByTag == null)
                        includeByTag = new Dictionary<string, bool>();

                    foreach (var tag in exclude.Tags)
                    {
                        includeByTag[tag] = false;
                    }
                }

                if (foundDefault)
                    break;

                type = type.BaseType;
                if (type == objType || type == null)
                    break;
            }

            return new TagAttributesData
            {
                IncludeByDefault = includeByDefault,
                IncludeByTag = includeByTag,
            };
        }

        private MetricTypeInfo GetMetricTypeInfo()
        {
            var type = GetType();
            MetricTypeInfo info;
            if (_typeInfoCache.TryGetValue(type, out info))
                return info;

            lock (_typeInfoLock)
            {
                if (_typeInfoCache.TryGetValue(type, out info))
                    return info;
                
                var needsPreSerialize = type.GetMethod(nameof(PreSerialize), BindingFlags.Instance | BindingFlags.NonPublic).DeclaringType != typeof(BosunMetric);
                var isExternalCounter = false; // todo - fix when ExternalCounter is merged in

                info = _typeInfoCache[type] = new MetricTypeInfo
                {
                    NeedsPreSerialize = needsPreSerialize,
                    IsExternalCounter = isExternalCounter,
                };

                return info;
            }
        }
    }
}
