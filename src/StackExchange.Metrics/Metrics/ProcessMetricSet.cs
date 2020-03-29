using System.Diagnostics;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="IMetricSet" /> to provide basic process metrics:
    ///  - CPU time
    ///  - Virtual memory
    ///  - Paged memory
    ///  - Threads
    /// </summary>
    public sealed class ProcessMetricSet : IMetricSet
    {
        private SamplingGauge _processorTime;
        private SamplingGauge _virtual;
        private SamplingGauge _paged;
        private SamplingGauge _threads;

        /// <summary>
        /// Constructs a new instance of <see cref="ProcessMetricSet" />.
        /// </summary>
        public ProcessMetricSet() { }

        /// <inheritdoc/>
        public void Initialize(IMetricsCollector collector)
        {
            _processorTime = collector.CreateMetric<SamplingGauge>("dotnet.cpu.processortime", "seconds", "Total processor time", includePrefix: false);
            _virtual = collector.CreateMetric<SamplingGauge>("dotnet.mem.virtual", "bytes", "Virtual memory for the process", includePrefix: false);
            _paged = collector.CreateMetric<SamplingGauge>("dotnet.mem.paged", "bytes", "Paged memory for the process", includePrefix: false);
            _threads = collector.CreateMetric<SamplingGauge>("dotnet.cpu.threads", "threads", "Threads for the process", includePrefix: false);
        }

        /// <inheritdoc/>
        public void Snapshot()
        {
            using (var proc = Process.GetCurrentProcess())
            {
                _processorTime.Record((long)proc.TotalProcessorTime.TotalSeconds);
                _virtual.Record(proc.VirtualMemorySize64);
                _paged.Record(proc.PagedMemorySize64);
                _threads.Record(proc.Threads.Count);
            }
        }
    }
}
