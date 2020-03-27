using System;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Metrics.Infrastructure;

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
        /// Gets the <see cref="MetricsCollectorOptions"/> for this builder.
        /// </summary>
        MetricsCollectorOptions Options { get; }

        /// <summary>
        /// Adds a <see cref="MetricEndpoint" /> to the collector.
        /// </summary>
        IMetricsCollectorBuilder AddEndpoint(string name, IMetricHandler handler);
        /// <summary>
        /// Adds a default tag to the collector.
        /// </summary>
        IMetricsCollectorBuilder AddDefaultTag(string name, string key);
        /// <summary>
        /// Configures options for the collector.
        /// </summary>
        IMetricsCollectorBuilder Configure(Action<MetricsCollectorOptions> configure);
    }
}
