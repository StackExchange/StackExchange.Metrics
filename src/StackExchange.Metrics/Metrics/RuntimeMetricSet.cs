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

            var cpuUsage = collector.CreateMetric<SamplingGauge>("cpu.usage", "percent", "% CPU usage");
            var workingSet = collector.CreateMetric<SamplingGauge>("mem.working_set", "bytes", "Working set for the process");

            AddGaugeCallback("cpu-usage", cpuUsage);
            AddGaugeCallback("working-set", workingSet);

            // GC
            var gen0 = collector.CreateMetric<Counter>("mem.collections.gen0", "collections", "Number of gen-0 collections");
            var gen1 = collector.CreateMetric<Counter>("mem.collections.gen1", "collections", "Number of gen-1 collections");
            var gen2 = collector.CreateMetric<Counter>("mem.collections.gen2", "collections", "Number of gen-2 collections");
            var gen0Size = collector.CreateMetric<SamplingGauge>("mem.size.gen0", "bytes", "Total number of bytes in gen-0");
            var gen1Size = collector.CreateMetric<SamplingGauge>("mem.size.gen1", "bytes", "Total number of bytes in gen-1");
            var gen2Size = collector.CreateMetric<SamplingGauge>("mem.size.gen2", "bytes", "Total number of bytes in gen-2");
            var heapSize = collector.CreateMetric<SamplingGauge>("mem.size.heap", "bytes", "Total number of bytes across all heaps");
            var lohSize = collector.CreateMetric<SamplingGauge>("mem.size.loh", "bytes", "Total number of bytes in the LOH");
            var allocRate = collector.CreateMetric<Counter>("mem.allocation_rate", "bytes/sec", "Allocation Rate (Bytes / sec)");

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
            var threadPoolCount = collector.CreateMetric<SamplingGauge>("threadpool.count", "threads", "Number of threads in the threadpool");
            var threadPoolQueueLength = collector.CreateMetric<SamplingGauge>("threadpool.queue_length", "workitems", "Number of work items queued to the threadpool");
            var timerCount = collector.CreateMetric<SamplingGauge>("timers.count", "timers", "Number of active timers");

            AddGaugeCallback("threadpool-thread-count", threadPoolCount);
            AddGaugeCallback("threadpool-queue-length", threadPoolQueueLength);
            AddGaugeCallback("active-timer-count", timerCount);
        }

        /// <inheritdoc/>
        public void Snapshot()
        {
        }
    }
}
#endif
