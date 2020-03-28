using System;
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
        public async static Task DumpAsync(this MetricsCollector collector, TextWriter textWriter, string[] excludedTags = null)
        {
            excludedTags = excludedTags ?? Array.Empty<string>();
            await textWriter.WriteLineAsync("DefaultTags:");
            var defaultTags = collector.DefaultTags;
            if (defaultTags.Count > 0)
            {
                foreach (var t in defaultTags)
                {
                    await textWriter.WriteAsync("  ");
                    await textWriter.WriteAsync(t.Key);
                    await textWriter.WriteAsync(" = ");
                    await textWriter.WriteAsync(t.Value);
                }
            }

            await textWriter.WriteLineAsync("");
            await textWriter.WriteLineAsync("----");
            await textWriter.WriteLineAsync("Endpoints:");

            var endpoints = collector.Endpoints;
            foreach (var endpoint in endpoints)
            {
                await textWriter.WriteAsync("  ");
                switch (endpoint.Handler)
                {
                    case BosunMetricHandler bosunHandler:
                        await textWriter.WriteLineAsync($"{endpoint.Name}: {bosunHandler.BaseUri?.AbsoluteUri ?? "null"}");
                        break;
                    case SignalFxMetricHandler signalFxHandler:
                        await textWriter.WriteLineAsync($"{endpoint.Name}: {signalFxHandler.BaseUri?.AbsoluteUri ?? "null"}");
                        break;
                    case LocalMetricHandler h:
                        await textWriter.WriteLineAsync($"{endpoint.Name}: Local");
                        break;
                }
            }

            await textWriter.WriteLineAsync("----");
            await textWriter.WriteLineAsync("Counters:");

            var count = 0;
            var localHandler = endpoints.Select(x => x.Handler).OfType<LocalMetricHandler>().FirstOrDefault();
            if (localHandler != null)
            {
                foreach (var reading in localHandler.GetReadings(reset: false).OrderBy(r => r.NameWithSuffix))
                {
                    await textWriter.WriteAsync("Name = ");
                    await textWriter.WriteAsync(reading.Name);
                    await textWriter.WriteAsync(reading.Suffix);
                    await textWriter.WriteAsync(", Value = ");
                    await textWriter.WriteAsync(reading.Value.ToString());
                    await textWriter.WriteAsync(", Type = ");
                    await textWriter.WriteAsync(reading.Type.ToString());
                    await textWriter.WriteAsync(", Tags = [");

                    bool firstTag = false;
                    foreach (var tag in reading.Tags)
                    {
                        // Skip excluded tags
                        if (excludedTags.Contains(tag.Key))
                        {
                            continue;
                        }

                        if (!firstTag)
                        {
                            firstTag = false;
                        }
                        else
                        {
                            await textWriter.WriteAsync(", ");
                        }
                        await textWriter.WriteAsync(tag.Key);
                        await textWriter.WriteAsync(" = ");
                        await textWriter.WriteAsync(tag.Value);
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
