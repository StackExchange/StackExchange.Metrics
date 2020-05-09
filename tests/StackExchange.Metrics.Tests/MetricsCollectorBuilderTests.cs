using System;
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
                builder => builder.AddSource(metricSource)
            );

            Assert.Contains(options.Sources, x => ReferenceEquals(x, metricSource));
        }

        [Fact]
        public void Sources_RuntimeSourceAdded()
        {
            var options = CreateOptions(
                builder => builder.AddRuntimeMetricSource()
            );

            Assert.Contains(options.Sources, x => x is RuntimeMetricSource);
        }

        [Fact]
        public void Sources_ProcessSourceAdded()
        {
            var options = CreateOptions(
                builder => builder.AddProcessMetricSource()
            );

            Assert.Contains(options.Sources, x => x is ProcessMetricSource);
        }

        [Fact]
        public void Sources_AspNetSourceAdded()
        {
            var options = CreateOptions(
                builder => builder.AddAspNetMetricSource()
            );

            Assert.Contains(options.Sources, x => x is AspNetMetricSource);
        }

        [Fact]
        public void Sources_DefaultSourcesAdded()
        {
            var options = CreateOptions(
                builder => builder.AddDefaultSources()
            );

            Assert.Contains(options.Sources, x => x is AspNetMetricSource);
            Assert.Contains(options.Sources, x => x is RuntimeMetricSource);
            Assert.Contains(options.Sources, x => x is ProcessMetricSource);
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
            var tagValueConverter = new TagValueTransformerDelegate((name, value) => value.ToString());
            var tagNameConverter = new Func<string, string>(x => x);
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

        private MetricsCollectorOptions CreateOptions(Action<IMetricsCollectorBuilder> configure = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITestOutputHelper>(_output);
            services.AddSingleton(typeof(ILogger<>), typeof(TestOutputLogger<>));
            services.AddSingleton<IDiagnosticsCollector, DiagnosticsCollector>();
            services.AddSingleton<MetricSourceOptions>();

            var builder = new MetricsCollectorBuilder(services);

            configure?.Invoke(builder);

            var serviceProvider = services.BuildServiceProvider();
            return builder.Build(serviceProvider);
        }
    }
}
