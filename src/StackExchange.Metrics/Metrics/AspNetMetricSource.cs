#if NETCOREAPP
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="MetricSource" /> to provide information for
    /// ASP.NET Core applications:
    ///  - Requests per second
    ///  - Total requests
    ///  - Current requests
    ///  - Failed requests
    /// </summary>
    public sealed class AspNetMetricSource : MetricSource
    {
        /// <summary>
        /// Constructs a new instance of <see cref="AspNetMetricSource" />.
        /// </summary>
        public AspNetMetricSource(IDiagnosticsCollector diagnosticsCollector, MetricSourceOptions options) : base(options)
        {
            const string MicrosoftAspNetCoreHostingEventSourceName = "Microsoft.AspNetCore.Hosting";

            diagnosticsCollector.AddSource(
                new EventPipeProvider(
                    MicrosoftAspNetCoreHostingEventSourceName,
                    EventLevel.Informational,
                    arguments: new Dictionary<string, string>()
                    {
                        { "EventCounterIntervalSec", "5" }
                    }
                )
            );

            void AddCounterCallback(string eventName, string name, string unit, string description)
            {
                var counter = AddCounter(name, unit, description);
                diagnosticsCollector.AddCounterCallback(MicrosoftAspNetCoreHostingEventSourceName, eventName, counter.Increment);
            }

            void AddGaugeCallback(string eventName, string name, string unit, string description)
            {
                var gauge = AddSamplingGauge(name, unit, description);
                diagnosticsCollector.AddGaugeCallback(MicrosoftAspNetCoreHostingEventSourceName, eventName, gauge.Record);
            }

            AddCounterCallback("requests-per-second", "dotnet.kestrel.requests.per_sec", "requests/sec", "Requests per second");
            AddGaugeCallback("total-requests", "dotnet.kestrel.requests.total", "requests", "Total requests");
            AddGaugeCallback("current-requests", "dotnet.kestrel.requests.current", "requests", "Currently executing requests");
            AddGaugeCallback("failed-requests", "dotnet.kestrel.requests.failed", "requests", "Failed requests");
        }
    }
}
#endif
