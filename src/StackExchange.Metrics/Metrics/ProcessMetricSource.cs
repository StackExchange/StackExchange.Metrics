using System.Collections.Generic;
using System.Diagnostics;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="MetricSource" /> to provide basic process metrics:
    ///  - CPU time
    ///  - Virtual memory
    ///  - Paged memory
    ///  - Threads
    /// </summary>
    public sealed class ProcessMetricSource : MetricSource
    {
        private readonly SamplingGauge _processorTime;
        private readonly SamplingGauge _virtualMemory;
        private readonly SamplingGauge _pagedMemory;
        private readonly SamplingGauge _threadCount;

        /// <summary>
        /// Constructs a new instance of <see cref="ProcessMetricSource" />.
        /// </summary>
        public ProcessMetricSource(MetricSourceOptions options) : base(options)
        {
            _processorTime = AddSamplingGauge("dotnet.cpu.processortime", "seconds", "Total processor time");
            _virtualMemory = AddSamplingGauge("dotnet.mem.virtual", "bytes", "Virtual memory for the process");
            _pagedMemory = AddSamplingGauge("dotnet.mem.paged", "bytes", "Paged memory for the process");
            _threadCount = AddSamplingGauge("dotnet.cpu.threads", "threads", "Threads for the process");
        }

        /// <inheritdoc/>
        public override void Attach(IMetricsCollector collector)
        {
            collector.BeforeSerialization += Snapshot;
        }

        /// <inheritdoc/>
        public override void Detach(IMetricsCollector collector)
        {
            collector.BeforeSerialization -= Snapshot;
        }

        private void Snapshot()
        {
            using (var process = Process.GetCurrentProcess())
            {
                _processorTime.Record(process.TotalProcessorTime.TotalSeconds);
                _virtualMemory.Record(process.VirtualMemorySize64);
                _pagedMemory.Record(process.PagedMemorySize64);
                _threadCount.Record(process.Threads.Count);
            }
        }
    }
}
