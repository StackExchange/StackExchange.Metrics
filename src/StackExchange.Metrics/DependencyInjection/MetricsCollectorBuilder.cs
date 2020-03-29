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
        private readonly ImmutableDictionary<string, string>.Builder _defaultTags;
        private Action<MetricsCollectorOptions> _configure;

        public MetricsCollectorBuilder(IServiceCollection services)
        {
            _metricEndpoints = ImmutableArray.CreateBuilder<MetricEndpoint>();
            _metricEndpoints.Add(new MetricEndpoint("Local", new LocalMetricHandler()));
            _defaultTags = ImmutableDictionary.CreateBuilder<string, string>();
            _defaultTags.Add("host", NameTransformers.Sanitize(Environment.MachineName));

            Options = new MetricsCollectorOptions();
            Services = services;
        }

        public IServiceCollection Services { get; }
        public MetricsCollectorOptions Options { get; }

        public IMetricsCollectorBuilder AddEndpoint(string name, IMetricHandler handler)
        {
            _metricEndpoints.Add(new MetricEndpoint(name, handler));
            return this;
        }

        public IMetricsCollectorBuilder AddDefaultTag(string key, string value)
        {
            _defaultTags.Add(key, value);
            return this;
        }

        public IMetricsCollectorBuilder Configure(Action<MetricsCollectorOptions> configure)
        {
            _configure = configure;
            return this;
        }

        public MetricsCollectorOptions Build(IServiceProvider serviceProvider)
        {
            Options.Sets = serviceProvider.GetServices<IMetricSet>();
            Options.Endpoints = _metricEndpoints.ToImmutable();
            Options.DefaultTags = _defaultTags.ToImmutable();
            _configure?.Invoke(Options);
            return Options;
        }
    }
}
