using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BosunReporter.Metrics;

namespace BosunReporter.Infrastructure
{
    /// <summary>
    /// The base class for all metrics (time series). Custom metric types may inherit from this directly. However, most users will want to inherit from a child
    /// class, such as <see cref="Counter"/> or <see cref="AggregateGauge"/>.
    /// </summary>
    public abstract class BosunMetric
    {
        struct MetricTypeInfo
        {
            public bool NeedsPreSerialize;
            public bool IsExternalCounter;
        }

        static readonly Dictionary<Type, MetricTypeInfo> _typeInfoCache = new Dictionary<Type, MetricTypeInfo>();
        static readonly object _typeInfoLock = new object();

        static readonly string[] s_singleEmptyStringArray = {""};

        /// <summary>
        /// The type of metric. Must be one of "counter", "gauge", or "rate".
        /// </summary>
        public abstract string MetricType { get; }
        /// <summary>
        /// The collector that this metric is attached to. A metric must be attached to a collector in order to be recorded on.
        /// </summary>
        public MetricsCollector Collector { get; internal set; }
        /// <summary>
        /// True if this metric is attached to a collector. A metric must be attached to a collector in order to be recorded on.
        /// </summary>
        public bool IsAttached { get; internal set; }

        HashSet<string> _suffixSet;
        /// <summary>
        /// An enumeration of metric name suffixes. In most cases, this will be a single-element collection where the value of the element is an empty string.
        /// However, some metric types may actually serialize as multiple time series distinguished by metric names with different suffixes. The only built-in
        /// metric type which does this is <see cref="AggregateGauge"/> where the suffixes will be things like "_avg", "_min", "_95", etc.
        /// </summary>
        public IReadOnlyCollection<string> Suffixes => Array.AsReadOnly(SuffixesArray);
        internal string[] SuffixesArray { get; private set; }
        internal int SuffixCount => SuffixesArray.Length;

        string _tagsJson;
        internal string TagsJson => _tagsJson ?? (_tagsJson = GetTagsJson(Collector.DefaultTags, Collector.TagValueConverter, Collector.TagsByTypeCache));

        string _name;
        readonly object _nameLock = new object();

        /// <summary>
        /// The metric name, including the global prefix (if applicable), but not including any suffixes (see <see cref="Suffixes"/>).
        /// </summary>
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

        /// <summary>
        /// Description of this metric (time series) which will be sent to Bosun as metadata.
        /// </summary>
        public virtual string Description { get; set; }
        /// <summary>
        /// The units for this metric (time series) which will be sent to Bosun as metadata. (example: "milliseconds")
        /// </summary>
        public virtual string Unit { get; internal set; }

        /// <summary>
        /// If true, the metric's value will be immediately serialized after initialization. This is useful for counters where you want a zero value to be
        /// recorded in Bosun every time the app restarts.
        /// </summary>
        public virtual bool SerializeInitialValue => MetricType == "counter";

        /// <summary>
        /// Instantiates the base class.
        /// </summary>
        protected BosunMetric()
        {
        }

        internal MetricKey GetMetricKey(string tagsJson = null)
        {
            return new MetricKey(_name, tagsJson ?? TagsJson);
        }

        // this method is only used when default tags are updated
        internal void SwapTagsJson(string tagsJson)
        {
            _tagsJson = tagsJson;
        }

        /// <summary>
        /// Called once per suffix in order to get a description.
        /// </summary>
        public virtual string GetDescription(int suffixIndex)
        {
            return Description;
        }

        /// <summary>
        /// Called once per suffix in order to get the units.
        /// </summary>
        public virtual string GetUnit(int suffixIndex)
        {
            return Unit;
        }

        /// <summary>
        /// Returns an enumerable of <see cref="MetaData"/> which which describes this metric.
        /// </summary>
        public virtual IEnumerable<MetaData> GetMetaData()
        {
            for (var i = 0; i < SuffixesArray.Length; i++)
            {
                var fullName = Name + SuffixesArray[i];

                yield return new MetaData(fullName, "rate", null, MetricType);

                var desc = GetDescription(i);
                if (!string.IsNullOrEmpty(desc))
                    yield return new MetaData(fullName, "desc", TagsJson, desc);

                var unit = GetUnit(i);
                if (!string.IsNullOrEmpty(unit))
                    yield return new MetaData(fullName, "unit", null, unit);
            }
        }

        /// <summary>
        /// This method will be called once to get an array of suffixes applicable to this metric. The returned array must never be modified after it is
        /// returned.
        /// </summary>
        protected virtual string[] GetImmutableSuffixesArray()
        {
            return s_singleEmptyStringArray;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SerializeInternal(MetricWriter writer, DateTime now)
        {
            Serialize(writer, now);
        }

        /// <summary>
        /// Called when metrics should be serialized to a payload. You must call <see cref="WriteValue"/> in order for anything to be serialized.
        ///  
        /// This is called in serial with all other metrics, so DO NOT do anything computationally expensive in this method. If you need to do expensive
        /// computations (e.g. sorting a bunch of data), do it in <see cref="PreSerialize"/> which is called in parallel prior to this method.
        /// </summary>
        /// <param name="writer">
        /// A reference to an opaque object. Pass this as the first parameter to <see cref="WriteValue"/>. DO NOT retain a reference to this object or use it
        /// in an asynchronous manner. It is only guaranteed to be in a valid state for the duration of this method call.
        /// </param>
        /// <param name="now">The timestamp when serialization of all metrics started.</param>
        protected abstract void Serialize(MetricWriter writer, DateTime now);

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

        /// <summary>
        /// If this method is overriden, it will be called shortly before <see cref="Serialize"/>. Unlike Serialize, which is called on all metrics in serial,
        /// PreSerialize is called in parallel, which makes it better place to do computationally expensive operations.
        /// </summary>
        protected virtual void PreSerialize()
        {
        }

        internal void LoadSuffixes()
        {
            SuffixesArray = GetImmutableSuffixesArray();
            _suffixSet = new HashSet<string>(SuffixesArray);
        }

        /// <summary>
        /// Throws an exception if <see cref="IsAttached"/> is false. It is best practice for all metrics to call this method before recording any values.
        /// </summary>
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

        /// <summary>
        /// This method serializes a time series record/value. This method must only be called from within <see cref="Serialize"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void WriteValue(MetricWriter writer, double value, DateTime now, int suffixIndex = 0)
        {
            writer.AddMetric(Name, SuffixesArray[suffixIndex], value, TagsJson, now);
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
                if (!IsExternalCounter())
                {
                    throw new InvalidOperationException(
                        $"At least one tag value must be specified for every metric. {GetType().FullName} was instantiated without any tag values.");
                }

                sb.Append('{');
            }
            else
            {
                sb[0] = '{'; // replaces the first comma
            }

            sb.Append('}');
            return sb.ToString();
        }

        List<BosunTag> GetTagsList(ReadOnlyDictionary<string, string> defaultTags, Dictionary<Type, List<BosunTag>> tagsByTypeCache)
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

            if (tags.Count == 0 && !IsExternalCounter())
                throw new TypeInitializationException(type.FullName, new Exception("Type does not contain any Bosun tags. Metrics must have at least one tag to be serializable."));

            tags.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            Collector.TagsByTypeCache[type] = tags;
            return tags;
        }

        struct TagAttributesData
        {
            public bool IncludeByDefault;
            public Dictionary<string, bool> IncludeByTag;
        }

        static TagAttributesData GetTagAttributesData(Type type)
        {
            var foundDefault = false;
            var includeByDefault = true;
            Dictionary<string, bool> includeByTag = null;

            var objType = typeof(object);

            while (true)
            {
                var exclude = type.GetCustomAttribute<ExcludeDefaultTagsAttribute>(false);
                var restore = type.GetCustomAttribute<RestoreDefaultTagsAttribute>(false);

                if (restore?.Tags.Length == 0)
                {
                    foundDefault = true;
                    includeByDefault = true;
                }
                else if (exclude?.Tags.Length == 0)
                {
                    foundDefault = true;
                    includeByDefault = false;
                }

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

        MetricTypeInfo GetMetricTypeInfo()
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
                var isExternalCounter = typeof(ExternalCounter).IsAssignableFrom(type);

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
