using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace StackExchange.Metrics.Benchmarks
{
    /// <summary>
    /// BenchmarkDotNet configuration that tests against BosunReporter, SE.Metrics previous and current.
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
