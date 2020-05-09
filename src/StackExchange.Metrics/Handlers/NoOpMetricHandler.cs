using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Handlers
{
    internal sealed class NoOpMetricHandler : IMetricHandler
    {
        public static readonly NoOpMetricHandler Instance = new NoOpMetricHandler();

        private NoOpMetricHandler()
        {
        }

        public IMetricReadingBatch BeginBatch() => NoOpBatch.Instance;

        public void Dispose()
        {
        }

        public ValueTask FlushAsync(TimeSpan delayBetweenRetries, int maxRetries, Action<AfterSendInfo> afterSend, Action<Exception> exceptionHandler) => default;

        public void SerializeMetadata(IEnumerable<Metadata> metadata)
        {
        }

        public void SerializeMetric(in MetricReading reading)
        {
        }

        private class NoOpBatch : IMetricReadingBatch
        {
            public static readonly NoOpBatch Instance = new NoOpBatch();

            private NoOpBatch()
            {
            }

            public long BytesWritten => 0;

            public long MetricsWritten => 0;

            public void Add(in MetricReading reading)
            {
            }
        }
    }
}
