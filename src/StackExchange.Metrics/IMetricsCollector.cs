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
        /// </summary>
        /// <param name="name">The metric name. If <paramref name="includePrefix"/>, global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        /// <param name="includePrefix">Whether the <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.</param>
        T CreateMetric<T>(string name, string unit, string description, Func<T> metricFactory, bool includePrefix = true) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. An exception will be thrown if a metric by the same name and tag values already exists.
        /// </summary>
        /// <param name="name">The metric name. If <paramref name="includePrefix"/>, global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        /// <param name="includePrefix">Whether the <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.</param>
        T CreateMetric<T>(string name, string unit, string description, T metric = null, bool includePrefix = true) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned.
        /// </summary>
        /// <param name="name">The metric name. If <paramref name="includePrefix"/>, global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metricFactory">A delegate which will be called to instantiate the metric.</param>
        /// <param name="includePrefix">Whether the <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.</param>
        T GetMetric<T>(string name, string unit, string description, Func<T> metricFactory, bool includePrefix = true) where T : MetricBase;

        /// <summary>
        /// Creates a metric (time series) and adds it to the collector. If a metric by the same name and tag values already exists, then that metric is
        /// returned.
        /// </summary>
        /// <param name="name">The metric name. If <paramref name="includePrefix"/>, global prefix <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended.</param>
        /// <param name="unit">The units of the metric (e.g. "milliseconds").</param>
        /// <param name="description">The metadata description of the metric.</param>
        /// <param name="metric">A pre-instantiated metric, or null if the metric type has a default constructor.</param>
        /// <param name="includePrefix">Whether the <see cref="MetricsCollectorOptions.MetricsNamePrefix"/> will be prepended to the metric name.</param>
        T GetMetric<T>(string name, string unit, string description, T metric = null, bool includePrefix = true) where T : MetricBase;
    }
}
