using System;
using System.ComponentModel;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics
{
    public partial class MetricsCollector
    {
        /// <summary>
        /// Obsolete - please use <see cref="BindMetric(string, string, Type, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void BindMetric(string name, string unit, Type type) => BindMetric(name, unit, type, true);

        /// <summary>
        /// Obsolete - please use <see cref="BindMetric(string, string, Type, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void BindMetricWithoutPrefix(string name, string unit, Type type) => BindMetric(name, unit, type, false);


        /// <summary>
        /// Obsolete - please use <see cref="TryGetMetricInfo(string, out Type, out string, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool TryGetMetricInfo(string name, out Type type, out string unit) => TryGetMetricInfo(name, out type, out unit, true);

        /// <summary>
        /// Obsolete - please use <see cref="TryGetMetricInfo(string, out Type, out string, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool TryGetMetricWithoutPrefixInfo(string name, out Type type, out string unit) => TryGetMetricInfo(name, out type, out unit, false);


        /// <summary>
        /// Obsolete - please use <see cref="CreateMetric{T}(string, string, string, Func{T}, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T CreateMetric<T>(string name, string unit, string description, Func<T> metricFactory) where T : MetricBase =>
            CreateMetric(name, unit, description, metricFactory, includePrefix: true);

        /// <summary>
        /// Obsolete - please use <see cref="CreateMetric{T}(string, string, string, Func{T}, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T CreateMetricWithoutPrefix<T>(string name, string unit, string description, Func<T> metricFactory) where T : MetricBase =>
            CreateMetric(name, unit, description, metricFactory, includePrefix: false);


        /// <summary>
        /// Obsolete - please use <see cref="CreateMetric{T}(string, string, string, T, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T CreateMetric<T>(string name, string unit, string description, T metric) where T : MetricBase =>
            CreateMetric(name, unit, description, metric, includePrefix: true);

        /// <summary>
        /// Obsolete - please use <see cref="CreateMetric{T}(string, string, string, T, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T CreateMetricWithoutPrefix<T>(string name, string unit, string description, T metric) where T : MetricBase =>
            CreateMetric(name, unit, description, metric, includePrefix: false);


        /// <summary>
        /// Obsolete - please use <see cref="GetMetric{T}(string, string, string, Func{T}, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T GetMetric<T>(string name, string unit, string description, Func<T> metricFactory) where T : MetricBase =>
            GetMetric(name, unit, description, metricFactory, includePrefix: true);

        /// <summary>
        /// Obsolete - please use <see cref="GetMetric{T}(string, string, string, Func{T}, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T GetMetricWithoutPrefix<T>(string name, string unit, string description, Func<T> metricFactory) where T : MetricBase =>
            GetMetric(name, unit, description, metricFactory, includePrefix: false);


        /// <summary>
        /// Obsolete - please use <see cref="GetMetric{T}(string, string, string, T, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T GetMetric<T>(string name, string unit, string description, T metric) where T : MetricBase =>
            GetMetric(name, unit, description, metric, includePrefix: true);

        /// <summary>
        /// Obsolete - please use <see cref="GetMetric{T}(string, string, string, T, bool)"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T GetMetricWithoutPrefix<T>(string name, string unit, string description, T metric) where T : MetricBase =>
            GetMetric(name, unit, description, metric, includePrefix: false);
    }
}
