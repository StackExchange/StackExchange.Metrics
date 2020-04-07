using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Metrics.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Metrics.Tests
{
    public class ServiceCollectionTests
    {
        private readonly ITestOutputHelper _output;

        public ServiceCollectionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void MetricsCollector_IsAddedAsSingleton()
        {
            var services = CreateServices();
            services.AddMetricsCollector();

            Assert.Contains(
                services,
                descriptor => descriptor.Lifetime == ServiceLifetime.Singleton &&
                              descriptor.ServiceType == typeof(MetricsCollector)
            );

            Assert.Contains(
                services,
                descriptor => descriptor.Lifetime == ServiceLifetime.Singleton &&
                              descriptor.ServiceType == typeof(IMetricsCollector)
            );
        }

        [Fact]
        public void DiagnosticsCollector_IsAddedAsSingleton()
        {
            var services = CreateServices();
            services.AddMetricsCollector();

            Assert.Contains(
                services,
                descriptor => descriptor.Lifetime == ServiceLifetime.Singleton &&
                              descriptor.ServiceType == typeof(DiagnosticsCollector)
            );

            Assert.Contains(
                services,
                descriptor => descriptor.Lifetime == ServiceLifetime.Singleton &&
                              descriptor.ServiceType == typeof(IDiagnosticsCollector)
            );
        }

        [Fact]
        public void MetricsCollectorOptions_IsAddedAsSingleton()
        {
            var services = CreateServices();
            services.AddMetricsCollector();

            Assert.Contains(
                services,
                descriptor => descriptor.Lifetime == ServiceLifetime.Singleton &&
                              descriptor.ServiceType == typeof(IOptions<MetricsCollectorOptions>)
            );
        }

        [Fact]
        public void MetricsService_IsAddedAsSingleton()
        {
            var services = CreateServices();
            services.AddMetricsCollector();

            Assert.Contains(
                services,
                descriptor => descriptor.Lifetime == ServiceLifetime.Singleton &&
                              descriptor.ServiceType == typeof(MetricsService)
            );
        }

        [Fact]
        public void MetricsService_IsAddedAsHostedService()
        {
            var services = CreateServices();
            services.AddMetricsCollector();

            var serviceProvider = services.BuildServiceProvider();

            var hostedServices = serviceProvider.GetServices<IHostedService>();

            Assert.Contains(hostedServices, x => x is MetricsService);
        }

        [Fact]
        public void DiagnosticsCollector_IsAddedAsHostedService()
        {
            var services = CreateServices();
            services.AddMetricsCollector();

            var serviceProvider = services.BuildServiceProvider();

            var hostedServices = serviceProvider.GetServices<IHostedService>();

            Assert.Contains(hostedServices, x => x is DiagnosticsCollector);
        }

        private ServiceCollection CreateServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITestOutputHelper>(_output);
            services.AddSingleton(typeof(ILogger<>), typeof(TestOutputLogger<>));
            return services;
        }
    }
}
