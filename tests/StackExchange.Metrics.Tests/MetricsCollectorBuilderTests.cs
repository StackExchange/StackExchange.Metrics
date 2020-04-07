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
        public void Sets_Added()
        {
            var metricSet = new TestMetricSet();
            var options = CreateOptions(
                builder => builder.AddSet(metricSet)
            );

            Assert.Contains(options.Sets, x => ReferenceEquals(x, metricSet));
        }

        [Fact]
        public void Sets_RuntimeSetAdded()
        {
            var options = CreateOptions(
                builder => builder.AddRuntimeMetricSet()
            );

            Assert.Contains(options.Sets, x => x is RuntimeMetricSet);
        }

        [Fact]
        public void Sets_ProcessSetAdded()
        {
            var options = CreateOptions(
                builder => builder.AddProcessMetricSet()
            );

            Assert.Contains(options.Sets, x => x is ProcessMetricSet);
        }

        [Fact]
        public void Sets_AspNetSetAdded()
        {
            var options = CreateOptions(
                builder => builder.AddAspNetMetricSet()
            );

            Assert.Contains(options.Sets, x => x is AspNetMetricSet);
        }

        [Fact]
        public void Sets_DefaultSetsAdded()
        {
            var options = CreateOptions(
                builder => builder.AddDefaultSets()
            );

            Assert.Contains(options.Sets, x => x is AspNetMetricSet);
            Assert.Contains(options.Sets, x => x is RuntimeMetricSet);
            Assert.Contains(options.Sets, x => x is ProcessMetricSet);
        }

        [Fact]
        public void Tags_HostTagAddedByDefault()
        {
            var options = CreateOptions();

            Assert.Contains(options.DefaultTags, x => x.Key == "host" && x.Value == NameTransformers.Sanitize(Environment.MachineName));
        }

        [Fact]
        public void Tags_HostTagCanBeOverridden()
        {
            var options = CreateOptions(
                builder => builder.AddDefaultTag("host", "bob")
            );

            Assert.Contains(options.DefaultTags, x => x.Key == "host" && x.Value == "bob");
        }

        [Fact]
        public void Tags_DefaultTagCanBeAdded()
        {
            var options = CreateOptions(
                builder => builder.AddDefaultTag("name", "value")
            );

            Assert.Contains(options.DefaultTags, x => x.Key == "name" && x.Value == "value");
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
            var tagValueConverter = new TagValueConverterDelegate((name, value) => value);
            var tagNameConverter = new Func<string, string>(x => x);
            var options = CreateOptions(
                builder => builder.Configure(
                    o =>
                    {
                        o.FlushInterval = TimeSpan.Zero;
                        o.MetricsNamePrefix = "tests.";
                        o.PropertyToTagName = tagNameConverter;
                        o.RetryInterval = TimeSpan.Zero;
                        o.SnapshotInterval = TimeSpan.Zero;
                        o.TagValueConverter = tagValueConverter;
                        o.ThrowOnPostFail = true;
                        o.ThrowOnQueueFull = true;
                    })
            );

            Assert.Equal(TimeSpan.Zero, options.FlushInterval);
            Assert.Equal("tests.", options.MetricsNamePrefix);
            Assert.StrictEqual(tagNameConverter, options.PropertyToTagName);
            Assert.Equal(TimeSpan.Zero, options.RetryInterval);
            Assert.Equal(TimeSpan.Zero, options.SnapshotInterval);
            Assert.StrictEqual(tagValueConverter, options.TagValueConverter);
            Assert.True(options.ThrowOnQueueFull);
            Assert.True(options.ThrowOnPostFail);

        }

        private MetricsCollectorOptions CreateOptions(Action<IMetricsCollectorBuilder> configure = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITestOutputHelper>(_output);
            services.AddSingleton(typeof(ILogger<>), typeof(TestOutputLogger<>));
            services.AddSingleton<IDiagnosticsCollector, DiagnosticsCollector>();

            var builder = new MetricsCollectorBuilder(services);

            configure?.Invoke(builder);

            var serviceProvider = services.BuildServiceProvider();
            return builder.Build(serviceProvider);
        }
    }
}
