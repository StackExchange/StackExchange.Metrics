#if NETCOREAPP
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="MetricSource" /> to provide information for
    /// the .NET Core runtime:
    ///  - CPU usage
    ///  - Working set
    ///  - GC counts
    ///  - GC sizes
    ///  - GC time
    ///  - LOH size
    ///  - Threadpool threads
    ///  - Threadpool queue lengths
    ///  - Exception counts
    /// </summary>
    /// <remarks>
    /// For where these are generated in the .NET Core runtime, see the defined counters at:
    /// https://github.com/dotnet/runtime/blob/5eda36ed557d789d888647745782b261472b9fa3/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/RuntimeEventSource.cs
    /// </remarks>
    public sealed class RuntimeMetricSource : MetricSource
    {
        /// <summary>
        /// Constructs a new instance of <see cref="RuntimeMetricSource" />.
        /// </summary>
        public RuntimeMetricSource(IDiagnosticsCollector diagnosticsCollector, MetricSourceOptions options) : base(options)
        {
            const string SystemRuntimeEventSourceName = "System.Runtime";

            diagnosticsCollector.AddSource(
                new EventPipeProvider(
                    SystemRuntimeEventSourceName,
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
                diagnosticsCollector.AddCounterCallback(SystemRuntimeEventSourceName, eventName, counter.Increment);
                Add(counter);
            }

            void AddGaugeCallback(string eventName, string name, string unit, string description)
            {
                var gauge = AddSamplingGauge(name, unit, description);
                diagnosticsCollector.AddGaugeCallback(SystemRuntimeEventSourceName, eventName, gauge.Record);
                Add(gauge);
            }

            AddGaugeCallback("cpu-usage", "dotnet.cpu.usage", "percent", "% CPU usage");
            AddGaugeCallback("working-set", "dotnet.mem.working_set", "megabytes", "Working set for the process");

            // GC
            AddCounterCallback("gen-0-gc-count", "dotnet.mem.collections.gen0", "collections", "Number of gen-0 collections");
            AddCounterCallback("gen-1-gc-count", "dotnet.mem.collections.gen1", "collections", "Number of gen-1 collections");
            AddCounterCallback("gen-2-gc-count", "dotnet.mem.collections.gen2", "collections", "Number of gen-2 collections");
            AddGaugeCallback("gen-0-size", "dotnet.mem.size.gen0", "bytes", "Total number of bytes in gen-0");
            AddGaugeCallback("gen-1-size", "dotnet.mem.size.gen1", "bytes", "Total number of bytes in gen-1");
            AddGaugeCallback("gen-2-size", "dotnet.mem.size.gen2", "bytes", "Total number of bytes in gen-2");
            AddGaugeCallback("gc-heap-size", "dotnet.mem.size.heap", "megabytes", "Total number of bytes across all heaps");
            AddGaugeCallback("loh-size", "dotnet.mem.size.loh", "bytes", "Total number of bytes in the LOH");
            AddCounterCallback("alloc-rate", "dotnet.mem.allocation_rate", "bytes/sec", "Allocation Rate (Bytes / sec)");

            // thread pool
            AddGaugeCallback("threadpool-thread-count", "dotnet.threadpool.count", "threads", "Number of threads in the threadpool");
            AddGaugeCallback("threadpool-queue-length", "dotnet.threadpool.queue_length", "workitems", "Number of work items queued to the threadpool");
            AddGaugeCallback("active-timer-count", "dotnet.timers.count", "timers", "Number of active timers");
        }
    }
}
#endif
