using System.Threading.Tasks;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Tests
{
    public class TestMetricSet : IMetricSet
    {
        private readonly TaskCompletionSource<object> _initializeTask;
        private readonly TaskCompletionSource<object> _snapshotTask;

        public Task InitializeTask => _initializeTask.Task;

        public Task SnapshotTask => _snapshotTask.Task;

        public TestMetricSet()
        {
            _initializeTask = new TaskCompletionSource<object>();
            _snapshotTask = new TaskCompletionSource<object>();
        }

        public void Initialize(IMetricsCollector collector) => _initializeTask.TrySetResult(null);

        public void Snapshot() => _snapshotTask.TrySetResult(null);
    }
}
