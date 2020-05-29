using System.Threading.Tasks;

namespace StackExchange.Metrics.Tests
{
    public class TestMetricSource : MetricSource
    {
        private readonly TaskCompletionSource<object> _attachTask;
        private readonly TaskCompletionSource<object> _detachTask;
        private readonly TaskCompletionSource<object> _snapshotTask;

        public Task AttachTask => _attachTask.Task;
        public Task DetachTask => _detachTask.Task;
        public Task SnapshotTask => _snapshotTask.Task;

        public TestMetricSource() : base(TestCreationOptions.Value)
        {
            _attachTask = new TaskCompletionSource<object>();
            _detachTask = new TaskCompletionSource<object>();
            _snapshotTask = new TaskCompletionSource<object>();
        }

        public override void Attach(IMetricsCollector collector)
        {
            _attachTask.TrySetResult(null);
            collector.BeforeSerialization += OnSnapshot;
        }

        private void OnSnapshot() => _snapshotTask.TrySetResult(null);

        public override void Detach(IMetricsCollector collector)
        {
            _detachTask.TrySetResult(null);
            collector.BeforeSerialization -= OnSnapshot;
        }
    }
}
