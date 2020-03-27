using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace StackExchange.Metrics
{
    /// <summary>
    /// <see cref="IHostedService" /> that manages the lifetime of a <see cref="MetricsCollector" /> instance.
    /// </summary>
    public class MetricsService : IHostedService
    {
        private readonly IOptions<MetricsCollectorOptions> _options;

        /// <summary>
        /// Instantiates a new <see cref="MetricsService" />.
        /// </summary>
        public MetricsService(IOptions<MetricsCollectorOptions> options)
        {
            _options = options;
        }

        private MetricsCollector _collector;

        /// <summary>
        /// Gets the <see cref="MetricsCollector" /> wrapped by this service.
        /// </summary>
        public MetricsCollector Collector
        {
            get
            {
                if (_collector == null)
                {
                    throw new InvalidOperationException("Service has not been successfully started");
                }

                return _collector;
            }
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _collector = new MetricsCollector(_options.Value);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _collector?.Shutdown();
            _collector = null;
            return Task.CompletedTask;
        }
    }
}
