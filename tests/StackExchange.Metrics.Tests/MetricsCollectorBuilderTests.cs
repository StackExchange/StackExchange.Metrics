using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Metrics.DependencyInjection;
using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Metrics.Tests
{
    public class MetricsCollectorBuilderTests
    {
        private readonly ITestOutputHelper _output;

        public MetricsCollectorBuilderTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Endpoints_LocalAddedByDefault()
        {
            var options = CreateOptions();

            Assert.Contains(options.Endpoints, e => e.Handler is LocalMetricHandler);
        }

        [Fact]
        public void Endpoints_AreAdded()
        {
            var options = CreateOptions(
                builder => builder.AddEndpoint("Test", new TestMetricHandler((handler, type, buffer) => default))
            );

            Assert.Contains(options.Endpoints, e => e.Name == "Test" && e.Handler is TestMetricHandler);
        }

        [Fact]
        public void Endpoints_BosunAdded()
        {
            var baseUri = new Uri("http://localhost");
            var options = CreateOptions(
                builder => builder.AddBosunEndpoint(baseUri)
            );

            Assert.Contains(options.Endpoints, e => e.Name == "Bosun" && e.Handler is BosunMetricHandler bosunHandler && bosunHandler.BaseUri == baseUri);
        }

        [Fact]
        public void Endpoints_SignalFxAdded()
        {
            var baseUri = new Uri("http://localhost");
            var options = CreateOptions(
                builder => builder.AddSignalFxEndpoint(baseUri)
            );

            Assert.Contains(options.Endpoints, e => e.Name == "SignalFx" && e.Handler is SignalFxMetricHandler signalFxHandler && signalFxHandler.BaseUri == baseUri);
        }

        [Fact]
        public void Sources_Added()
        {
            var metricSource = new TestMetricSource();
            var options = CreateOptions(
                builder => {
                    builder.ClearSources();
                    builder.AddSource(metricSource);
                }
            );

            Assert.Contains(options.Sources, x => ReferenceEquals(x, metricSource));
        }

        [Fact]
        public void Sources_RuntimeSourceAdded()
        {
            var options = CreateOptions(
                builder =>
                {
                    builder.ClearSources();
                    builder.AddRuntimeMetricSource();
                }
            );

            Assert.Single(options.Sources);
            Assert.Contains(options.Sources, x => x is RuntimeMetricSource);
        }

        [Fact]
        public void Sources_RuntimeSourceAddedOnce()
        {
            var options = CreateOptions(
                builder =>
                {
                    builder.ClearSources();
                    builder.AddRuntimeMetricSource();
                    builder.AddRuntimeMetricSource();
                }
            );

            Assert.Single(options.Sources);
            Assert.Contains(options.Sources, x => x is RuntimeMetricSource);
        }

        [Fact]
        public void Sources_ProcessSourceAdded()
        {
            var options = CreateOptions(
                builder =>
                {
                    builder.ClearSources();
                    builder.AddProcessMetricSource();
                }
            );

            Assert.Single(options.Sources);
            Assert.Contains(options.Sources, x => x is ProcessMetricSource);
        }

        [Fact]
        public void Sources_ProcessSourceAddedOnce()
        {
            var options = CreateOptions(
                builder =>
                {
                    builder.ClearSources();
                    builder.AddProcessMetricSource();
                    builder.AddProcessMetricSource();
                }
            );

            Assert.Single(options.Sources);
            Assert.Contains(options.Sources, x => x is ProcessMetricSource);
        }

        [Fact]
        public void Sources_AspNetSourceAdded()
        {
            var options = CreateOptions(
                builder =>
                {
                    builder.ClearSources();
                    builder.AddAspNetMetricSource();
                }
            );

            Assert.Single(options.Sources);
            Assert.Contains(options.Sources, x => x is AspNetMetricSource);
        }

        [Fact]
        public void Sources_AspNetSourceAddedOnce()
        {
            var options = CreateOptions(
                builder =>
                {
                    builder.ClearSources();
                    builder.AddAspNetMetricSource();
                    builder.AddAspNetMetricSource();
                }
            );

            Assert.Single(options.Sources);
            Assert.Contains(options.Sources, x => x is AspNetMetricSource);
        }

        [Fact]
        public void Sources_DefaultSourcesAdded()
        {
            var options = CreateOptions(
                builder =>
                {
                    builder.ClearSources();
                    builder.AddDefaultSources();
                }
            );

            Assert.Equal(3, options.Sources.Count());
            Assert.Contains(options.Sources, x => x is AspNetMetricSource);
            Assert.Contains(options.Sources, x => x is RuntimeMetricSource);
            Assert.Contains(options.Sources, x => x is ProcessMetricSource);
        }

        [Fact]
        public void Sources_DefaultSourcesAddedOnce()
        {
            var options = CreateOptions(
                builder =>
                {
                    builder.ClearSources();
                    builder.AddDefaultSources();
                    builder.AddDefaultSources();
                }
            );

            Assert.Equal(3, options.Sources.Count());
            Assert.Contains(options.Sources, x => x is AspNetMetricSource);
            Assert.Contains(options.Sources, x => x is RuntimeMetricSource);
            Assert.Contains(options.Sources, x => x is ProcessMetricSource);
        }

        [Fact]
        public void Sources_Clear ()
        {
            var options = CreateOptions(
                builder =>
                {
                    builder.ClearSources();
                }
            );

            Assert.Empty(options.Sources);
        }

        [Fact]
        public void ExceptionHandler_IsConfigured()
        {
            var handler = new Action<Exception>(ex => _output.WriteLine(ex.ToString()));

            var options = CreateOptions(
                builder => builder.UseExceptionHandler(handler)
            );

            Assert.StrictEqual(handler, options.ExceptionHandler);
        }

        [Fact]
        public void Configure_ChangesSettings()
        {
            var options = CreateOptions(
                builder => builder.Configure(
                    o =>
                    {
                        o.FlushInterval = TimeSpan.Zero;
                        o.RetryInterval = TimeSpan.Zero;
                        o.SnapshotInterval = TimeSpan.Zero;
                        o.ThrowOnPostFail = true;
                        o.ThrowOnQueueFull = true;
                    })
            );

            Assert.Equal(TimeSpan.Zero, options.FlushInterval);
            Assert.Equal(TimeSpan.Zero, options.RetryInterval);
            Assert.Equal(TimeSpan.Zero, options.SnapshotInterval);
            Assert.True(options.ThrowOnQueueFull);
            Assert.True(options.ThrowOnPostFail);

        }

        private ServiceCollection CreateServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITestOutputHelper>(_output);
            services.AddSingleton(typeof(ILogger<>), typeof(TestOutputLogger<>));
            services.AddSingleton<IDiagnosticsCollector, DiagnosticsCollector>();
            services.AddSingleton<MetricSourceOptions>();
            return services;
        }

        private MetricsCollectorOptions CreateOptions(Action<IMetricsCollectorBuilder> configure = null)
        {
            var services = CreateServices();
            var builder = new MetricsCollectorBuilder(services);

            configure?.Invoke(builder);

            var serviceProvider = services.BuildServiceProvider();
            return builder.Build(serviceProvider);
        }
    }
}
