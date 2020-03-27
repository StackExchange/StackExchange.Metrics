using System;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Exposes functionality to create new metrics and to collect metrics.
    /// </summary>
    public partial interface IMetricsCollector
    {
        /// <summary>
        /// The length of time between metric reports (snapshots).
        /// </summary>
        TimeSpan ReportingInterval { get; }

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        T CreateMetric<T>(string name, string unit, string description, Func<T> metricFactory) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        T CreateMetric<T>(string name, string unit, string description, T metric = null) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will NOT be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        T CreateMetricWithoutPrefix<T>(string name, string unit, string description, Func<T> metricFactory) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// The <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will NOT be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        T CreateMetricWithoutPrefix<T>(string name, string unit, string description, T metric = null) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned. The <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        T GetMetric<T>(string name, string unit, string description, Func<T> metricFactory) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned. The <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        T GetMetric<T>(string name, string unit, string description, T metric = null) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned. The <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will NOT be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        T GetMetricWithoutPrefix<T>(string name, string unit, string description, Func<T> metricFactory) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned. The <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will NOT be prepended to the metric name.
        /// </summary>
        /// <param name="name">The metric name. The global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        T GetMetricWithoutPrefix<T>(string name, string unit, string description, T metric = null) where T : MetricBase;
    }
}
