using System;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;

namespace StackExchange.Metrics.DependencyInjection
{
    internal sealed class MetricsCollectorBuilder : IMetricsCollectorBuilder
    {       
        private readonly MetricsCollectorOptions _options;
        private readonly ImmutableArray<MetricEndpoint>.Builder _metricEndpoints;
        private readonly ImmutableDictionary<string, string>.Builder _defaultTags;
        private Action<MetricsCollectorOptions> _configure;

        public MetricsCollectorBuilder(IServiceCollection services)
        {
            _metricEndpoints = ImmutableArray.CreateBuilder<MetricEndpoint>();
            _metricEndpoints.Add(new MetricEndpoint("Local", new LocalMetricHandler()));
            _defaultTags = ImmutableDictionary.CreateBuilder<string, string>();
            _defaultTags.Add("host", NameTransformers.Sanitize(Environment.MachineName));
            _options = new MetricsCollectorOptions();

            Services = services;
        }

        public IServiceCollection Services { get; }

        public IMetricsCollectorBuilder AddEndpoint(string name, IMetricHandler handler)
        {
            _metricEndpoints.Add(new MetricEndpoint(name, handler));
            return this;
        }

        public IMetricsCollectorBuilder AddSet<T>() where T : class, IMetricSet
        {
            Services.AddTransient<IMetricSet, T>();
            return this;
        }

        public IMetricsCollectorBuilder AddSet<T>(T set) where T : class, IMetricSet
        {
            Services.AddTransient<IMetricSet>(_ => set);
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

        public IMetricsCollectorBuilder UseExceptionHandler(Action<Exception> handler)
        {
            _options.ExceptionHandler = handler;
            return this;
        }

        public MetricsCollectorOptions Build(IServiceProvider serviceProvider)
        {
            _options.Sets = serviceProvider.GetServices<IMetricSet>();
            _options.Endpoints = _metricEndpoints.ToImmutable();
            _options.DefaultTags = _defaultTags.ToImmutable();
            _configure?.Invoke(_options);
            return _options;
        }
    }
}
