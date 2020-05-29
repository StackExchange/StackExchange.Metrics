using System;
using StackExchange.Metrics;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Metrics;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for <see cref="IMetricsCollectorBuilder" />.
    /// </summary>
    public static class MetricsCollectorBuilderExtensions
    {
        /// <summary>
        /// Adds the default built-in <see cref="MetricSource" /> implementations to the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddDefaultSources(this IMetricsCollectorBuilder builder)
        {
            return builder.AddProcessMetricSource()
#if NETCOREAPP
                .AddAspNetMetricSource()
                .AddRuntimeMetricSource()
#else
                .AddGarbageCollectorMetricSource();
#endif
            ;
        }

        /// <summary>
        /// Adds a <see cref="ProcessMetricSource" /> to the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddProcessMetricSource(this IMetricsCollectorBuilder builder) => builder.AddSource<ProcessMetricSource>();

#if NETCOREAPP
        /// <summary>
        /// Adds a <see cref="RuntimeMetricSource" /> to the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddRuntimeMetricSource(this IMetricsCollectorBuilder builder) => builder.AddSource<RuntimeMetricSource>();

        /// <summary>
        /// Adds a <see cref="AspNetMetricSource" /> to the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddAspNetMetricSource(this IMetricsCollectorBuilder builder) => builder.AddSource<AspNetMetricSource>();
#else
        /// <summary>
        /// Adds a <see cref="GarbageCollectorMetricSource" /> to the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddGarbageCollectorMetricSource(this IMetricsCollectorBuilder builder) => builder.AddSource<GarbageCollectorMetricSource>();
#endif

        /// <summary>
        /// Adds a Bosun endpoint to the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddBosunEndpoint(this IMetricsCollectorBuilder builder, Uri baseUri, Action<BosunMetricHandler> configure = null)
        {
            var handler = new BosunMetricHandler(baseUri);
            configure?.Invoke(handler);
            return builder.AddEndpoint("Bosun", handler);
        }

        /// <summary>
        /// Adds a SignalFx endpoint to the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddSignalFxEndpoint(this IMetricsCollectorBuilder builder, Uri baseUri, Action<SignalFxMetricHandler> configure = null)
        {
            var handler = new SignalFxMetricHandler(baseUri);
            configure?.Invoke(handler);
            return builder.AddEndpoint("SignalFx", handler);
        }

        /// <summary>
        /// Adds a SignalFx endpoint to the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddSignalFxEndpoint(this IMetricsCollectorBuilder builder, Uri baseUri, string accessToken, Action<SignalFxMetricHandler> configure = null)
        {
            var handler = new SignalFxMetricHandler(baseUri, accessToken);
            configure?.Invoke(handler);
            return builder.AddEndpoint("SignalFx", handler);
        }

        /// <summary>
        /// Exceptions which occur on a background thread will be passed to the delegate specified here.
        /// </summary>
        public static IMetricsCollectorBuilder UseExceptionHandler(this IMetricsCollectorBuilder builder, Action<Exception> handler)
        {
            builder.Options.ExceptionHandler = handler;
            return builder;
        }

        /// <summary>
        /// Registers an <see cref="MetricSource" /> for use with the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddSource<T>(this IMetricsCollectorBuilder builder) where T : MetricSource
        {
            builder.Services.AddSingleton<T>();
            builder.Services.AddSingleton<MetricSource>(s => s.GetService<T>());
            return builder;
        }

        /// <summary>
        /// Registers an <see cref="MetricSource" /> for use with the collector.
        /// </summary>
        public static IMetricsCollectorBuilder AddSource<T>(this IMetricsCollectorBuilder builder, T source) where T : MetricSource
        {
            builder.Services.AddSingleton<T>(source);
            builder.Services.AddSingleton<MetricSource>(s => s.GetService<T>());
            return builder;
        }
    }
}
