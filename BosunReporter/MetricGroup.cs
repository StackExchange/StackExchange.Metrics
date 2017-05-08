using System;
using System.Collections.Generic;
using System.Reflection;
using BosunReporter.Infrastructure;

namespace BosunReporter
{
    public partial class MetricsCollector
    {
        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, TMetric> GetMetricGroup<T1, TMetric>(string name, string unit, string description, Func<T1, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, TMetric>(this, name, false, unit, description, metricFactory);
        }

        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values. The global metric name prefix
        /// <see cref="MetricsCollector.MetricsNamePrefix"/> will not be not be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, TMetric> GetMetricGroupWithoutPrefix<T1, TMetric>(string name, string unit, string description, Func<T1, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, TMetric>(this, name, true, unit, description, metricFactory);
        }
    }

    /// <summary>
    /// A set of time series which share the same metric name, but different tag sets.
    /// </summary>
    public class MetricGroup<T1, TMetric> where TMetric : BosunMetric
    {
        private readonly object _dictionaryLock = new object();
        private readonly MetricsCollector _collector;
        private readonly Dictionary<T1, TMetric> _metrics = new Dictionary<T1, TMetric>();
        private readonly Func<T1, TMetric> _metricFactory;

        /// <summary>
        /// The metric name shared by all time series in this group.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// If true, the metric name will not be prefixed with the global prefix.
        /// </summary>
        public bool WithoutPrefix { get; }
        /// <summary>
        /// The units for this metric group (such as "milliseconds").
        /// </summary>
        public string Unit { get; }
        /// <summary>
        /// The description for all time series in the group. This will be sent to Bosun as metadata.
        /// </summary>
        public string Description { get; }

        internal MetricGroup(MetricsCollector collector, string name, bool withoutPrefix, string unit, string description, Func<T1, TMetric> metricFactory = null)
        {
            _collector = collector;
            Name = name;
            WithoutPrefix = withoutPrefix;
            Unit = unit;
            Description = description;
            _metricFactory = metricFactory ?? GetDefaultFactory();
        }

        /// <summary>
        /// Gets a metric (time series) which already exists in the group. If the metric does not exist, an exception will be thrown.
        /// </summary>
        public TMetric this[T1 tag1]
        {
            get
            {
                return _metrics[tag1];
            }
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1)
        {
            bool isNew;
            return Add(tag1, out isNew);
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1, out bool isNew)
        {
            isNew = false;
            if (_metrics.ContainsKey(tag1))
                return _metrics[tag1];

            lock (_dictionaryLock)
            {
                if (_metrics.ContainsKey(tag1))
                    return _metrics[tag1];

                isNew = true;
                TMetric metric;
                if (WithoutPrefix)
                    metric = _collector.GetMetricWithoutPrefix(Name, Unit, Description, _metricFactory(tag1));
                else
                    metric = _collector.GetMetric(Name, Unit, Description, _metricFactory(tag1));

                _metrics[tag1] = metric;
                return metric;
            }
        }

        /// <summary>
        /// Returns true if a metric with the provided key(s) already exists in the group.
        /// </summary>
        public bool Contains(T1 tag1)
        {
            return _metrics.ContainsKey(tag1);
        }

        /// <summary>
        /// Returns the default factory delegate. This is the factory that will be used if the metricFactory parameter is null when instantiating the
        /// MetricGroup.
        /// </summary>
        public Func<T1, TMetric> GetDefaultFactory()
        {
            var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1) }, null);
            if (constructor == null)
            {
                throw new Exception(
                    $"Cannot create a MetricGroup for Type \"{typeof(TMetric).FullName}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
                    "Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.");
            }

            return (tag1) => (TMetric)constructor.Invoke(new object[] { tag1 });
        }

        /// <summary>
        /// This method may only be called if T1 is an enum type. It calls Add() for every enum value of T1.
        /// </summary>
        /// <param name="includeObsolete">If false, enum values marked as obsolete will not be added to the metric group.</param>
        public void PopulateFromEnum(bool includeObsolete = true)
        {
            var type = typeof(T1);
            if (!type.IsEnum)
                throw new Exception("PopulateFromEnum can only be called when the type argument T1 is an enum type.");

            foreach (var val in Enum.GetValues(type))
            {
                if (!includeObsolete)
                {
                    var field = type.GetField(val.ToString(), BindingFlags.Static | BindingFlags.Public);
                    if (field.GetCustomAttribute<ObsoleteAttribute>() != null)
                        continue;
                }

                Add((T1)val);
            }
        }

    }

    public partial class MetricsCollector
    {
        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, T2, TMetric> GetMetricGroup<T1, T2, TMetric>(string name, string unit, string description, Func<T1, T2, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, T2, TMetric>(this, name, false, unit, description, metricFactory);
        }

        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values. The global metric name prefix
        /// <see cref="MetricsCollector.MetricsNamePrefix"/> will not be not be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, T2, TMetric> GetMetricGroupWithoutPrefix<T1, T2, TMetric>(string name, string unit, string description, Func<T1, T2, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, T2, TMetric>(this, name, true, unit, description, metricFactory);
        }
    }

    /// <summary>
    /// A set of time series which share the same metric name, but different tag sets.
    /// </summary>
    public class MetricGroup<T1, T2, TMetric> where TMetric : BosunMetric
    {
        private readonly object _dictionaryLock = new object();
        private readonly MetricsCollector _collector;
        private readonly Dictionary<Tuple<T1, T2>, TMetric> _metrics = new Dictionary<Tuple<T1, T2>, TMetric>();
        private readonly Func<T1, T2, TMetric> _metricFactory;

        /// <summary>
        /// The metric name shared by all time series in this group.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// If true, the metric name will not be prefixed with the global prefix.
        /// </summary>
        public bool WithoutPrefix { get; }
        /// <summary>
        /// The units for this metric group (such as "milliseconds").
        /// </summary>
        public string Unit { get; }
        /// <summary>
        /// The description for all time series in the group. This will be sent to Bosun as metadata.
        /// </summary>
        public string Description { get; }

        internal MetricGroup(MetricsCollector collector, string name, bool withoutPrefix, string unit, string description, Func<T1, T2, TMetric> metricFactory = null)
        {
            _collector = collector;
            Name = name;
            WithoutPrefix = withoutPrefix;
            Unit = unit;
            Description = description;
            _metricFactory = metricFactory ?? GetDefaultFactory();
        }

        /// <summary>
        /// Gets a metric (time series) which already exists in the group. If the metric does not exist, an exception will be thrown.
        /// </summary>
        public TMetric this[T1 tag1, T2 tag2]
        {
            get
            {
                var key = new Tuple<T1, T2>(tag1, tag2);
                return _metrics[key];
            }
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1, T2 tag2)
        {
            bool isNew;
            return Add(tag1, tag2, out isNew);
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1, T2 tag2, out bool isNew)
        {
            isNew = false;
            var key = new Tuple<T1, T2>(tag1, tag2);
            if (_metrics.ContainsKey(key))
                return _metrics[key];

            lock (_dictionaryLock)
            {
                if (_metrics.ContainsKey(key))
                    return _metrics[key];

                isNew = true;
                TMetric metric;
                if (WithoutPrefix)
                    metric = _collector.GetMetricWithoutPrefix(Name, Unit, Description, _metricFactory(tag1, tag2));
                else
                    metric = _collector.GetMetric(Name, Unit, Description, _metricFactory(tag1, tag2));

                _metrics[key] = metric;
                return metric;
            }
        }

        /// <summary>
        /// Returns true if a metric with the provided key(s) already exists in the group.
        /// </summary>
        public bool Contains(T1 tag1, T2 tag2)
        {
            var key = new Tuple<T1, T2>(tag1, tag2);
            return _metrics.ContainsKey(key);
        }

        /// <summary>
        /// Returns the default factory delegate. This is the factory that will be used if the metricFactory parameter is null when instantiating the
        /// MetricGroup.
        /// </summary>
        public Func<T1, T2, TMetric> GetDefaultFactory()
        {
            var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1), typeof(T2) }, null);
            if (constructor == null)
            {
                throw new Exception(
                    $"Cannot create a MetricGroup for Type \"{typeof(TMetric).FullName}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
                    "Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.");
            }

            return (tag1, tag2) => (TMetric)constructor.Invoke(new object[] { tag1, tag2 });
        }


    }

    public partial class MetricsCollector
    {
        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, T2, T3, TMetric> GetMetricGroup<T1, T2, T3, TMetric>(string name, string unit, string description, Func<T1, T2, T3, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, T2, T3, TMetric>(this, name, false, unit, description, metricFactory);
        }

        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values. The global metric name prefix
        /// <see cref="MetricsCollector.MetricsNamePrefix"/> will not be not be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, T2, T3, TMetric> GetMetricGroupWithoutPrefix<T1, T2, T3, TMetric>(string name, string unit, string description, Func<T1, T2, T3, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, T2, T3, TMetric>(this, name, true, unit, description, metricFactory);
        }
    }

    /// <summary>
    /// A set of time series which share the same metric name, but different tag sets.
    /// </summary>
    public class MetricGroup<T1, T2, T3, TMetric> where TMetric : BosunMetric
    {
        private readonly object _dictionaryLock = new object();
        private readonly MetricsCollector _collector;
        private readonly Dictionary<Tuple<T1, T2, T3>, TMetric> _metrics = new Dictionary<Tuple<T1, T2, T3>, TMetric>();
        private readonly Func<T1, T2, T3, TMetric> _metricFactory;

        /// <summary>
        /// The metric name shared by all time series in this group.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// If true, the metric name will not be prefixed with the global prefix.
        /// </summary>
        public bool WithoutPrefix { get; }
        /// <summary>
        /// The units for this metric group (such as "milliseconds").
        /// </summary>
        public string Unit { get; }
        /// <summary>
        /// The description for all time series in the group. This will be sent to Bosun as metadata.
        /// </summary>
        public string Description { get; }

        internal MetricGroup(MetricsCollector collector, string name, bool withoutPrefix, string unit, string description, Func<T1, T2, T3, TMetric> metricFactory = null)
        {
            _collector = collector;
            Name = name;
            WithoutPrefix = withoutPrefix;
            Unit = unit;
            Description = description;
            _metricFactory = metricFactory ?? GetDefaultFactory();
        }

        /// <summary>
        /// Gets a metric (time series) which already exists in the group. If the metric does not exist, an exception will be thrown.
        /// </summary>
        public TMetric this[T1 tag1, T2 tag2, T3 tag3]
        {
            get
            {
                var key = new Tuple<T1, T2, T3>(tag1, tag2, tag3);
                return _metrics[key];
            }
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1, T2 tag2, T3 tag3)
        {
            bool isNew;
            return Add(tag1, tag2, tag3, out isNew);
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1, T2 tag2, T3 tag3, out bool isNew)
        {
            isNew = false;
            var key = new Tuple<T1, T2, T3>(tag1, tag2, tag3);
            if (_metrics.ContainsKey(key))
                return _metrics[key];

            lock (_dictionaryLock)
            {
                if (_metrics.ContainsKey(key))
                    return _metrics[key];

                isNew = true;
                TMetric metric;
                if (WithoutPrefix)
                    metric = _collector.GetMetricWithoutPrefix(Name, Unit, Description, _metricFactory(tag1, tag2, tag3));
                else
                    metric = _collector.GetMetric(Name, Unit, Description, _metricFactory(tag1, tag2, tag3));

                _metrics[key] = metric;
                return metric;
            }
        }

        /// <summary>
        /// Returns true if a metric with the provided key(s) already exists in the group.
        /// </summary>
        public bool Contains(T1 tag1, T2 tag2, T3 tag3)
        {
            var key = new Tuple<T1, T2, T3>(tag1, tag2, tag3);
            return _metrics.ContainsKey(key);
        }

        /// <summary>
        /// Returns the default factory delegate. This is the factory that will be used if the metricFactory parameter is null when instantiating the
        /// MetricGroup.
        /// </summary>
        public Func<T1, T2, T3, TMetric> GetDefaultFactory()
        {
            var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1), typeof(T2), typeof(T3) }, null);
            if (constructor == null)
            {
                throw new Exception(
                    $"Cannot create a MetricGroup for Type \"{typeof(TMetric).FullName}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
                    "Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.");
            }

            return (tag1, tag2, tag3) => (TMetric)constructor.Invoke(new object[] { tag1, tag2, tag3 });
        }


    }

    public partial class MetricsCollector
    {
        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, T2, T3, T4, TMetric> GetMetricGroup<T1, T2, T3, T4, TMetric>(string name, string unit, string description, Func<T1, T2, T3, T4, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, T2, T3, T4, TMetric>(this, name, false, unit, description, metricFactory);
        }

        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values. The global metric name prefix
        /// <see cref="MetricsCollector.MetricsNamePrefix"/> will not be not be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, T2, T3, T4, TMetric> GetMetricGroupWithoutPrefix<T1, T2, T3, T4, TMetric>(string name, string unit, string description, Func<T1, T2, T3, T4, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, T2, T3, T4, TMetric>(this, name, true, unit, description, metricFactory);
        }
    }

    /// <summary>
    /// A set of time series which share the same metric name, but different tag sets.
    /// </summary>
    public class MetricGroup<T1, T2, T3, T4, TMetric> where TMetric : BosunMetric
    {
        private readonly object _dictionaryLock = new object();
        private readonly MetricsCollector _collector;
        private readonly Dictionary<Tuple<T1, T2, T3, T4>, TMetric> _metrics = new Dictionary<Tuple<T1, T2, T3, T4>, TMetric>();
        private readonly Func<T1, T2, T3, T4, TMetric> _metricFactory;

        /// <summary>
        /// The metric name shared by all time series in this group.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// If true, the metric name will not be prefixed with the global prefix.
        /// </summary>
        public bool WithoutPrefix { get; }
        /// <summary>
        /// The units for this metric group (such as "milliseconds").
        /// </summary>
        public string Unit { get; }
        /// <summary>
        /// The description for all time series in the group. This will be sent to Bosun as metadata.
        /// </summary>
        public string Description { get; }

        internal MetricGroup(MetricsCollector collector, string name, bool withoutPrefix, string unit, string description, Func<T1, T2, T3, T4, TMetric> metricFactory = null)
        {
            _collector = collector;
            Name = name;
            WithoutPrefix = withoutPrefix;
            Unit = unit;
            Description = description;
            _metricFactory = metricFactory ?? GetDefaultFactory();
        }

        /// <summary>
        /// Gets a metric (time series) which already exists in the group. If the metric does not exist, an exception will be thrown.
        /// </summary>
        public TMetric this[T1 tag1, T2 tag2, T3 tag3, T4 tag4]
        {
            get
            {
                var key = new Tuple<T1, T2, T3, T4>(tag1, tag2, tag3, tag4);
                return _metrics[key];
            }
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1, T2 tag2, T3 tag3, T4 tag4)
        {
            bool isNew;
            return Add(tag1, tag2, tag3, tag4, out isNew);
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1, T2 tag2, T3 tag3, T4 tag4, out bool isNew)
        {
            isNew = false;
            var key = new Tuple<T1, T2, T3, T4>(tag1, tag2, tag3, tag4);
            if (_metrics.ContainsKey(key))
                return _metrics[key];

            lock (_dictionaryLock)
            {
                if (_metrics.ContainsKey(key))
                    return _metrics[key];

                isNew = true;
                TMetric metric;
                if (WithoutPrefix)
                    metric = _collector.GetMetricWithoutPrefix(Name, Unit, Description, _metricFactory(tag1, tag2, tag3, tag4));
                else
                    metric = _collector.GetMetric(Name, Unit, Description, _metricFactory(tag1, tag2, tag3, tag4));

                _metrics[key] = metric;
                return metric;
            }
        }

        /// <summary>
        /// Returns true if a metric with the provided key(s) already exists in the group.
        /// </summary>
        public bool Contains(T1 tag1, T2 tag2, T3 tag3, T4 tag4)
        {
            var key = new Tuple<T1, T2, T3, T4>(tag1, tag2, tag3, tag4);
            return _metrics.ContainsKey(key);
        }

        /// <summary>
        /// Returns the default factory delegate. This is the factory that will be used if the metricFactory parameter is null when instantiating the
        /// MetricGroup.
        /// </summary>
        public Func<T1, T2, T3, T4, TMetric> GetDefaultFactory()
        {
            var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1), typeof(T2), typeof(T3), typeof(T4) }, null);
            if (constructor == null)
            {
                throw new Exception(
                    $"Cannot create a MetricGroup for Type \"{typeof(TMetric).FullName}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
                    "Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.");
            }

            return (tag1, tag2, tag3, tag4) => (TMetric)constructor.Invoke(new object[] { tag1, tag2, tag3, tag4 });
        }


    }

    public partial class MetricsCollector
    {
        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, T2, T3, T4, T5, TMetric> GetMetricGroup<T1, T2, T3, T4, T5, TMetric>(string name, string unit, string description, Func<T1, T2, T3, T4, T5, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, T2, T3, T4, T5, TMetric>(this, name, false, unit, description, metricFactory);
        }

        /// <summary>
        /// Creates a new metric group where each individual metric (time series) is of type <typeparamref name="TMetric"/>. The preceding type parameters
        /// describe the keys of the group, and typically represent different tag values. The global metric name prefix
        /// <see cref="MetricsCollector.MetricsNamePrefix"/> will not be not be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name for all time series in this group.</param>
        /// <param name="unit">The units for all time series in this group (e.g. "milliseconds").</param>
        /// <param name="description">The description for all time series in the group. This will be sent to Bosun as metadata.</param>
        /// <param name="metricFactory">
        /// The delegate used to instantiate new metrics (time series) within this group. This parameter may be omitted if <typeparamref name="TMetric"/> has a
        /// constructor with the same signature as this delegate parameter.
        /// </param>
        public MetricGroup<T1, T2, T3, T4, T5, TMetric> GetMetricGroupWithoutPrefix<T1, T2, T3, T4, T5, TMetric>(string name, string unit, string description, Func<T1, T2, T3, T4, T5, TMetric> metricFactory = null)
            where TMetric : BosunMetric
        {
            return new MetricGroup<T1, T2, T3, T4, T5, TMetric>(this, name, true, unit, description, metricFactory);
        }
    }

    /// <summary>
    /// A set of time series which share the same metric name, but different tag sets.
    /// </summary>
    public class MetricGroup<T1, T2, T3, T4, T5, TMetric> where TMetric : BosunMetric
    {
        private readonly object _dictionaryLock = new object();
        private readonly MetricsCollector _collector;
        private readonly Dictionary<Tuple<T1, T2, T3, T4, T5>, TMetric> _metrics = new Dictionary<Tuple<T1, T2, T3, T4, T5>, TMetric>();
        private readonly Func<T1, T2, T3, T4, T5, TMetric> _metricFactory;

        /// <summary>
        /// The metric name shared by all time series in this group.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// If true, the metric name will not be prefixed with the global prefix.
        /// </summary>
        public bool WithoutPrefix { get; }
        /// <summary>
        /// The units for this metric group (such as "milliseconds").
        /// </summary>
        public string Unit { get; }
        /// <summary>
        /// The description for all time series in the group. This will be sent to Bosun as metadata.
        /// </summary>
        public string Description { get; }

        internal MetricGroup(MetricsCollector collector, string name, bool withoutPrefix, string unit, string description, Func<T1, T2, T3, T4, T5, TMetric> metricFactory = null)
        {
            _collector = collector;
            Name = name;
            WithoutPrefix = withoutPrefix;
            Unit = unit;
            Description = description;
            _metricFactory = metricFactory ?? GetDefaultFactory();
        }

        /// <summary>
        /// Gets a metric (time series) which already exists in the group. If the metric does not exist, an exception will be thrown.
        /// </summary>
        public TMetric this[T1 tag1, T2 tag2, T3 tag3, T4 tag4, T5 tag5]
        {
            get
            {
                var key = new Tuple<T1, T2, T3, T4, T5>(tag1, tag2, tag3, tag4, tag5);
                return _metrics[key];
            }
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1, T2 tag2, T3 tag3, T4 tag4, T5 tag5)
        {
            bool isNew;
            return Add(tag1, tag2, tag3, tag4, tag5, out isNew);
        }

        /// <summary>
        /// Adds a metric to the group, if it doesn't already exist.
        /// </summary>
        /// <returns>The metric.</returns>
        public TMetric Add(T1 tag1, T2 tag2, T3 tag3, T4 tag4, T5 tag5, out bool isNew)
        {
            isNew = false;
            var key = new Tuple<T1, T2, T3, T4, T5>(tag1, tag2, tag3, tag4, tag5);
            if (_metrics.ContainsKey(key))
                return _metrics[key];

            lock (_dictionaryLock)
            {
                if (_metrics.ContainsKey(key))
                    return _metrics[key];

                isNew = true;
                TMetric metric;
                if (WithoutPrefix)
                    metric = _collector.GetMetricWithoutPrefix(Name, Unit, Description, _metricFactory(tag1, tag2, tag3, tag4, tag5));
                else
                    metric = _collector.GetMetric(Name, Unit, Description, _metricFactory(tag1, tag2, tag3, tag4, tag5));

                _metrics[key] = metric;
                return metric;
            }
        }

        /// <summary>
        /// Returns true if a metric with the provided key(s) already exists in the group.
        /// </summary>
        public bool Contains(T1 tag1, T2 tag2, T3 tag3, T4 tag4, T5 tag5)
        {
            var key = new Tuple<T1, T2, T3, T4, T5>(tag1, tag2, tag3, tag4, tag5);
            return _metrics.ContainsKey(key);
        }

        /// <summary>
        /// Returns the default factory delegate. This is the factory that will be used if the metricFactory parameter is null when instantiating the
        /// MetricGroup.
        /// </summary>
        public Func<T1, T2, T3, T4, T5, TMetric> GetDefaultFactory()
        {
            var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5) }, null);
            if (constructor == null)
            {
                throw new Exception(
                    $"Cannot create a MetricGroup for Type \"{typeof(TMetric).FullName}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
                    "Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.");
            }

            return (tag1, tag2, tag3, tag4, tag5) => (TMetric)constructor.Invoke(new object[] { tag1, tag2, tag3, tag4, tag5 });
        }


    }

}
