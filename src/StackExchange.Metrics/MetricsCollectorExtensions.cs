using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Metrics.Handlers;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Extension methods for <see cref="MetricsCollector" />
    /// </summary>
    public static class MetricsCollectorExtensions
    {
        /// <summary>
        /// Dumps the state of a <see cref="MetricsCollector" />.
        /// </summary>
        public async static Task DumpAsync(this MetricsCollector collector, TextWriter textWriter, MetricSourceOptions options)
        {
            await textWriter.WriteLineAsync("DefaultTags:");
            var defaultTags = options.DefaultTags;
            if (defaultTags.Count > 0)
            {
                foreach (var t in defaultTags)
                {
                    await textWriter.WriteAsync("  ");
                    await textWriter.WriteAsync(t.Key);
                    await textWriter.WriteAsync(" = ");
                    await textWriter.WriteAsync(t.Value);
                    await textWriter.WriteLineAsync(string.Empty);
                }
            }

            await textWriter.WriteLineAsync("----");
            await textWriter.WriteLineAsync("Endpoints:");

            var endpoints = collector.Endpoints;
            foreach (var endpoint in endpoints)
            {
                await textWriter.WriteAsync("  ");
                await textWriter.WriteAsync(endpoint.Name);
                await textWriter.WriteAsync(": ");
                switch (endpoint.Handler)
                {
                    case BosunMetricHandler bosunHandler:
                        await textWriter.WriteLineAsync(bosunHandler.BaseUri?.AbsoluteUri ?? "null");
                        break;
                    case SignalFxMetricHandler signalFxHandler:
                        await textWriter.WriteLineAsync(signalFxHandler.BaseUri?.AbsoluteUri ?? "null");
                        break;
                    case LocalMetricHandler h:
                        await textWriter.WriteLineAsync("Local");
                        break;
                    default:
                        await textWriter.WriteLineAsync("Unknown");
                        break;
                }
            }

            await textWriter.WriteLineAsync("----");
            await textWriter.WriteLineAsync("Counters:");

            var count = 0;
            var localHandler = endpoints.Select(x => x.Handler).OfType<LocalMetricHandler>().FirstOrDefault();
            if (localHandler != null)
            {
                foreach (var reading in localHandler.GetReadings(reset: false).OrderBy(r => r.Name))
                {
                    await textWriter.WriteAsync("Name = ");
                    await textWriter.WriteAsync(reading.Name);
                    await textWriter.WriteAsync(", Value = ");
                    await textWriter.WriteAsync(reading.Value.ToString());
                    await textWriter.WriteAsync(", Type = ");
                    await textWriter.WriteAsync(reading.Type.ToString());
                    await textWriter.WriteAsync(", Tags = [");

                    var i = 0;
                    foreach (var tag in reading.Tags)
                    {
                        // Skip default tags
                        if (defaultTags.ContainsKey(tag.Key))
                        {
                            i++;
                            continue;
                        }

                        await textWriter.WriteAsync(tag.Key);
                        await textWriter.WriteAsync(" = ");
                        await textWriter.WriteAsync(tag.Value);
                        if (i++ < reading.Tags.Count - 1)
                        {
                            await textWriter.WriteAsync(", ");
                        }
                    }

                    await textWriter.WriteLineAsync("]");
                    count++;
                }
            }

            if (count == 0)
            {
                await textWriter.WriteLineAsync("No readings found yet - this is normal immediately after startup, GIVE IT A MINUTE.");
            }
        }
    }
}
