#if NETCOREAPP
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="IMetricSet" /> to provide information for
    /// ASP.NET Core applications:
    ///  - Requests per second
    ///  - Total requests
    ///  - Current requests
    ///  - Failed requests
    /// </summary>
    public sealed class AspNetMetricSet : IMetricSet
    {
        private readonly IDiagnosticsCollector _diagnosticsCollector;

        /// <summary>
        /// Constructs a new instance of <see cref="AspNetMetricSet" />.
        /// </summary>
        public AspNetMetricSet(IDiagnosticsCollector diagnosticsCollector)
        {
            _diagnosticsCollector = diagnosticsCollector;
        }

        /// <inheritdoc/>
        public void Initialize(IMetricsCollector collector)
        {
            const string MicrosoftAspNetCoreHostingEventSourceName = "Microsoft.AspNetCore.Hosting";

            _diagnosticsCollector.AddSource(
                new EventPipeProvider(
                    MicrosoftAspNetCoreHostingEventSourceName,
                    EventLevel.Informational,
                    arguments: new Dictionary<string, string>()
                    {
                        { "EventCounterIntervalSec", collector.ReportingInterval.TotalSeconds.ToString() }
                    }
                )
            );

            void AddCounterCallback(string name, Counter counter) => _diagnosticsCollector.AddCounterCallback(MicrosoftAspNetCoreHostingEventSourceName, name, counter.Increment);
            void AddGaugeCallback(string name, SamplingGauge gauge) => _diagnosticsCollector.AddGaugeCallback(MicrosoftAspNetCoreHostingEventSourceName, name, gauge.Record);

            var requestsPerSec = collector.CreateMetric<Counter>("dotnet.kestrel.requests.per_sec", "requests/sec", "Requests per second", includePrefix: false);
            var totalRequests = collector.CreateMetric<SamplingGauge>("dotnet.kestrel.requests.total", "requests", "Total requests", includePrefix: false);
            var currentRequests = collector.CreateMetric<SamplingGauge>("dotnet.kestrel.requests.current", "requests", "Currently executing requests", includePrefix: false);
            var failedRequests = collector.CreateMetric<SamplingGauge>("dotnet.kestrel.requests.failed", "requests", "Failed requests", includePrefix: false);

            AddCounterCallback("requests-per-sec", requestsPerSec);
            AddGaugeCallback("total-requests", totalRequests);
            AddGaugeCallback("current-requests", currentRequests);
            AddGaugeCallback("failed-requests", failedRequests);
        }

        /// <inheritdoc/>
        public void Snapshot() { }
    }
}
#endif
