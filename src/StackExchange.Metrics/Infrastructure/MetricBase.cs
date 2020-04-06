using StackExchange.Metrics.Metrics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// The base class for all metrics (time series). Custom metric types may inherit from this directly. However, most users will want to inherit from a child
    /// class, such as <see cref="Counter"/> or <see cref="AggregateGauge"/>.
    /// </summary>
    public abstract class MetricBase
    {
        struct MetricTypeInfo
        {
            public bool NeedsPreSerialize;
        }

        static readonly Dictionary<Type, MetricTypeInfo> s_typeInfoCache = new Dictionary<Type, MetricTypeInfo>();

        static readonly string[] s_singleEmptyStringArray = {""};

        /// <summary>
        /// <see cref="MetricType" /> value indicating the type of metric.
        /// </summary>
        public abstract MetricType MetricType { get; }
        /// <summary>
        /// The collector that this metric is attached to. A metric must be attached to a collector in order to be recorded on.
        /// </summary>
        public MetricsCollector Collector { get; internal set; }
        /// <summary>
        /// True if this metric is attached to a collector. A metric must be attached to a collector in order to be recorded on.
        /// </summary>
        public bool IsAttached { get; internal set; }

        /// <summary>
        /// An enumeration of metric name suffixes. In most cases, this will be a single-element collection where the value of the element is an empty string.
        /// However, some metric types may actually serialize as multiple time series distinguished by metric names with different suffixes. The only built-in
        /// metric type which does this is <see cref="AggregateGauge"/> where the suffixes will be things like "_avg", "_min", "_95", etc.
        /// </summary>
        public IReadOnlyCollection<string> Suffixes => Array.AsReadOnly(SuffixesArray);
        internal string[] SuffixesArray { get; private set; }
        internal int SuffixCount => SuffixesArray.Length;

        IReadOnlyDictionary<string, string> _tags;
        internal IReadOnlyDictionary<string, string> Tags => _tags ??= GetTags(Collector.DefaultTags, Collector.TagValueConverter, Collector.PropertyToTagName, Collector.TagsByTypeCache);

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

                    if (value == null || !MetricValidation.IsValidMetricName(value))
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
        public virtual bool SerializeInitialValue => MetricType == MetricType.Counter || MetricType == MetricType.CumulativeCounter;

        /// <summary>
        /// Instantiates the base class.
        /// </summary>
        protected MetricBase()
        {
        }

        internal MetricKey GetMetricKey(IReadOnlyDictionary<string, string> tags = null)
        {
            return new MetricKey(_name, tags ?? Tags);
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

                var metricType = string.Empty;
                switch (MetricType)
                {
                    case MetricType.Counter:
                    case MetricType.CumulativeCounter:
                        metricType = "counter";
                        break;
                    case MetricType.Gauge:
                        metricType = "gauge";
                        break;
                    default:
                        metricType = MetricType.ToString().ToLower();
                        break;

                }

                yield return new MetaData(fullName, MetadataNames.Rate, null, metricType);

                var desc = GetDescription(i);
                if (!string.IsNullOrEmpty(desc))
                    yield return new MetaData(fullName, MetadataNames.Description, Tags, desc);

                var unit = GetUnit(i);
                if (!string.IsNullOrEmpty(unit))
                    yield return new MetaData(fullName, MetadataNames.Unit, null, unit);
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
        internal void SerializeInternal(IMetricBatch writer, DateTime now) => Serialize(writer, now);

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
        protected abstract void Serialize(IMetricBatch writer, DateTime now);

        internal bool NeedsPreSerializeCalled()
        {
            return GetMetricTypeInfo().NeedsPreSerialize;
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
                    ex.Data["Tags"] = string.Join(",", Tags);
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
        protected void WriteValue(IMetricBatch writer, double value, DateTime now, int suffixIndex = 0)
        {
            writer.SerializeMetric(
                new MetricReading(Name, MetricType, SuffixesArray[suffixIndex], value, Tags, now)
            );
        }

        internal IReadOnlyDictionary<string, string> GetTags(
            IReadOnlyDictionary<string, string> defaultTags,
            TagValueConverterDelegate tagValueConverter,
            Func<string, string> propertyToTagConverter,
            Dictionary<Type, List<MetricTag>> tagsByTypeCache)
        {
            var tags = new Dictionary<string, string>();
            foreach (var tag in GetTagsList(defaultTags, propertyToTagConverter, tagsByTypeCache))
            {
                var value = tag.IsFromDefault ? defaultTags[tag.Name] : tag.GetValue(this);
                if (tagValueConverter != null)
                    value = tagValueConverter(tag.Name, value);

                if (value == null)
                {
                    if (tag.IsOptional)
                        continue;

                    throw new InvalidOperationException(
                        $"null is not a valid tag value for {GetType().FullName}.{tag.MemberInfo.Name}. This tag was declared as non-optional.");
                }
                if (!MetricValidation.IsValidTagValue(value))
                {
                    throw new InvalidOperationException(
                        $"Invalid value for tag {GetType().FullName}.{tag.MemberInfo.Name}. \"{value}\" is not a valid tag value. " +
                        $"Only characters in the regex class [a-zA-Z0-9\\-_./] are allowed.");
                }

                tags.Add(tag.Name, value);
            }

            if (tags.Count == 0)
            {
                throw new InvalidOperationException(
                    $"At least one tag value must be specified for every metric. {GetType().FullName} was instantiated without any tag values.");
            }

            return tags;
        }

        List<MetricTag> GetTagsList(IReadOnlyDictionary<string, string> defaultTags, Func<string, string> propertyToTagConverter, Dictionary<Type, List<MetricTag>> tagsByTypeCache)
        {
            var type = GetType();
            if (tagsByTypeCache.ContainsKey(type))
                return tagsByTypeCache[type];

            // build list of tag members of the current type
            var members = type.GetMembers();
            var tags = new List<MetricTag>();
            foreach (var member in members)
            {
                var metricTag = member.GetCustomAttribute<MetricTagAttribute>();
                if (metricTag != null)
                    tags.Add(new MetricTag(member, metricTag, propertyToTagConverter));
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

                    tags.Add(new MetricTag(name));
                }
            }

            if (tags.Count == 0)
                throw new TypeInitializationException(type.FullName, new Exception("Type does not contain any Bosun tags. Metrics must have at least one tag to be serializable."));

            tags.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            tagsByTypeCache[type] = tags;
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
            if (s_typeInfoCache.TryGetValue(type, out var info))
                return info;

            lock (s_typeInfoCache)
            {
                if (s_typeInfoCache.TryGetValue(type, out info))
                    return info;

                var needsPreSerialize = type.GetMethod(nameof(PreSerialize), BindingFlags.Instance | BindingFlags.NonPublic).DeclaringType != typeof(MetricBase);

                info = s_typeInfoCache[type] = new MetricTypeInfo
                {
                    NeedsPreSerialize = needsPreSerialize,
                };

                return info;
            }
        }
    }
}
