#if NETCOREAPP
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="IMetricSet" /> to provide information for
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
    public sealed class RuntimeMetricSet : IMetricSet
    {
        private readonly IDiagnosticsCollector _diagnosticsCollector;

        /// <summary>
        /// Constructs a new instance of <see cref="RuntimeMetricSet" />.
        /// </summary>
        public RuntimeMetricSet(IDiagnosticsCollector diagnosticsCollector)
        {
            _diagnosticsCollector = diagnosticsCollector;
        }

        /// <inheritdoc/>
        public void Initialize(IMetricsCollector collector)
        {
            const string SystemRuntimeEventSourceName = "System.Runtime";

            _diagnosticsCollector.AddSource(
                new EventPipeProvider(
                    SystemRuntimeEventSourceName,
                    EventLevel.Informational,
                    arguments: new Dictionary<string, string>()
                    {
                        { "EventCounterIntervalSec", collector.ReportingInterval.TotalSeconds.ToString() }
                    }
                )
            );

            void AddCounterCallback(string name, Counter counter) => _diagnosticsCollector.AddCounterCallback(SystemRuntimeEventSourceName, name, counter.Increment);
            void AddGaugeCallback(string name, SamplingGauge gauge) => _diagnosticsCollector.AddGaugeCallback(SystemRuntimeEventSourceName, name, gauge.Record);

            var cpuUsage = collector.CreateMetric<SamplingGauge>("dotnet.cpu.usage", "percent", "% CPU usage", includePrefix: false);
            var workingSet = collector.CreateMetric<SamplingGauge>("dotnet.mem.working_set", "bytes", "Working set for the process", includePrefix: false);

            AddGaugeCallback("cpu-usage", cpuUsage);
            AddGaugeCallback("working-set", workingSet);

            // GC
            var gen0 = collector.CreateMetric<Counter>("dotnet.mem.collections.gen0", "collections", "Number of gen-0 collections", includePrefix: false);
            var gen1 = collector.CreateMetric<Counter>("dotnet.mem.collections.gen1", "collections", "Number of gen-1 collections", includePrefix: false);
            var gen2 = collector.CreateMetric<Counter>("dotnet.mem.collections.gen2", "collections", "Number of gen-2 collections", includePrefix: false);
            var gen0Size = collector.CreateMetric<SamplingGauge>("dotnet.mem.size.gen0", "bytes", "Total number of bytes in gen-0", includePrefix: false);
            var gen1Size = collector.CreateMetric<SamplingGauge>("dotnet.mem.size.gen1", "bytes", "Total number of bytes in gen-1", includePrefix: false);
            var gen2Size = collector.CreateMetric<SamplingGauge>("dotnet.mem.size.gen2", "bytes", "Total number of bytes in gen-2", includePrefix: false);
            var heapSize = collector.CreateMetric<SamplingGauge>("dotnet.mem.size.heap", "bytes", "Total number of bytes across all heaps", includePrefix: false);
            var lohSize = collector.CreateMetric<SamplingGauge>("dotnet.mem.size.loh", "bytes", "Total number of bytes in the LOH", includePrefix: false);
            var allocRate = collector.CreateMetric<Counter>("dotnet.mem.allocation_rate", "bytes/sec", "Allocation Rate (Bytes / sec)", includePrefix: false);

            AddGaugeCallback("gc-heap-size", heapSize);
            AddGaugeCallback("gen-0-size", gen0Size);
            AddGaugeCallback("gen-1-size", gen1Size);
            AddGaugeCallback("gen-2-size", gen2Size);
            AddCounterCallback("gen-0-gc-count", gen0);
            AddCounterCallback("gen-1-gc-count", gen1);
            AddCounterCallback("gen-2-gc-count", gen2);
            AddGaugeCallback("loh-size", lohSize);
            AddCounterCallback("alloc-rate", allocRate);

            // thread pool
            var threadPoolCount = collector.CreateMetric<SamplingGauge>("dotnet.threadpool.count", "threads", "Number of threads in the threadpool", includePrefix: false);
            var threadPoolQueueLength = collector.CreateMetric<SamplingGauge>("dotnet.threadpool.queue_length", "workitems", "Number of work items queued to the threadpool", includePrefix: false);
            var timerCount = collector.CreateMetric<SamplingGauge>("dotnet.timers.count", "timers", "Number of active timers", includePrefix: false);

            AddGaugeCallback("threadpool-thread-count", threadPoolCount);
            AddGaugeCallback("threadpool-queue-length", threadPoolQueueLength);
            AddGaugeCallback("active-timer-count", timerCount);
        }

        /// <inheritdoc/>
        public void Snapshot() { }
    }
}
#endif
