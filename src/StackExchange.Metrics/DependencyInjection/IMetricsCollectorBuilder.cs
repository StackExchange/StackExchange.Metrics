using System;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;

namespace StackExchange.Metrics.DependencyInjection
{
    /// <summary>
    /// Builder used to configure an <see cref="IMetricsCollector" />.
    /// </summary>
    public interface IMetricsCollectorBuilder
    {
        /// <summary>
        /// Gets the <see cref="IServiceCollection" /> where the collector is configured.
        /// </summary>
        IServiceCollection Services { get; }
        /// <summary>
        /// Instantiates and adds an <see cref="IMetricSet" /> to the collector.
        /// </summary>
        IMetricsCollectorBuilder AddSet<T>() where T : class, IMetricSet;
        /// <summary>
        /// Adds an <see cref="IMetricSet" /> to the collector.
        /// </summary>
        IMetricsCollectorBuilder AddSet<T>(T set) where T : class, IMetricSet;
        /// <summary>
        /// Adds a <see cref="MetricEndpoint" /> to the collector.
        /// </summary>
        IMetricsCollectorBuilder AddEndpoint(string name, IMetricHandler handler);
        /// <summary>
        /// Adds a default tag to the collector.
        /// </summary>
        IMetricsCollectorBuilder AddDefaultTag(string name, string key);
        /// <summary>
        /// Exceptions which occur on a background thread will be passed to the delegate specified here.
        /// </summary>
        /// <returns></returns>
        IMetricsCollectorBuilder UseExceptionHandler(Action<Exception> handler);
        /// <summary>
        /// Configures options for the collector.
        /// </summary>
        IMetricsCollectorBuilder Configure(Action<MetricsCollectorOptions> configure);
    }
}
