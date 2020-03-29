#if NETCOREAPP
using System;
using Microsoft.Diagnostics.NETCore.Client;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Exposes methods used to hook up diagnostics collection from the runtime
    /// in .NET Core.
    /// </summary>
    public interface IDiagnosticsCollector
    {
        /// <summary>
        /// Adds a source of diagnostic event data.
        /// </summary>
        /// <param name="source">
        /// An <see cref="EventPipeProvider" /> representing a source of diagnostic events.
        /// </param>
        void AddSource(EventPipeProvider source);

        /// <summary>
        /// Adds a callback used to handle counter data for a diagnostic event of the specified name.
        /// </summary>
        /// <param name="provider">
        /// Name of the diagnostic event's provider.
        /// </param>
        /// <param name="name">
        /// Name of a diagnostic event.
        /// </param>
        /// <param name="action">
        /// A callback used to increment a counter.
        /// </param>
        void AddCounterCallback(string provider, string name, Action<long> action);

        /// <summary>
        /// Adds a callback used to handle gauge data for a diagnostic event of the specified name.
        /// </summary>
        /// <param name="provider">
        /// Name of the diagnostic event's provider.
        /// </param>
        /// <param name="name">
        /// Name of a diagnostic event.
        /// </param>
        /// <param name="action">
        /// A callback used to record a gauge value.
        /// </param>
        void AddGaugeCallback(string provider, string name, Action<double> action);
    }
}
#endif
