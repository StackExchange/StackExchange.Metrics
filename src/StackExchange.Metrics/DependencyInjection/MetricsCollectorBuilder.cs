using System;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.DependencyInjection
{
    internal sealed class MetricsCollectorBuilder : IMetricsCollectorBuilder
    {       
        private readonly ImmutableArray<MetricEndpoint>.Builder _metricEndpoints;
        private Action<MetricsCollectorOptions> _configure;

        public MetricsCollectorBuilder(IServiceCollection services)
        {
            _metricEndpoints = ImmutableArray.CreateBuilder<MetricEndpoint>();
            _metricEndpoints.Add(new MetricEndpoint("Local", new LocalMetricHandler()));

            Options = new MetricsCollectorOptions();
            Services = services;

            this.ConfigureSources(
                o => o.DefaultTags.Add("host", Environment.MachineName)
            );
            this.AddDefaultSources();
        }

        public IServiceCollection Services { get; }
        public MetricsCollectorOptions Options { get; }

        public IMetricsCollectorBuilder AddEndpoint(string name, IMetricHandler handler)
        {
            _metricEndpoints.Add(new MetricEndpoint(name, handler));
            return this;
        }

        public IMetricsCollectorBuilder Configure(Action<MetricsCollectorOptions> configure)
        {
            _configure = configure;
            return this;
        }

        public MetricsCollectorOptions Build(IServiceProvider serviceProvider)
        {
            Options.Sources = serviceProvider.GetServices<MetricSource>();
            Options.Endpoints = _metricEndpoints.ToImmutable();
            _configure?.Invoke(Options);
            return Options;
        }
    }
}
