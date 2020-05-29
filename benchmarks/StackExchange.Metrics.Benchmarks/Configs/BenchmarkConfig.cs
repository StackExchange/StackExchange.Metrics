using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace StackExchange.Metrics.Benchmarks
{
    /// <summary>
    /// Shared BenchmarkDotNet configuration for all benchmarks.
    /// </summary>
    public class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            //Job.Default.WithRuntime(CoreRuntime.Core31);
            AddDiagnoser(MemoryDiagnoser.Default);
        }
    }
}
