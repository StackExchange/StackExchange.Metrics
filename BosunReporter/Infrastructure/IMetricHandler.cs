using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BosunReporter.Infrastructure
{
    /// <summary>
    /// Exposes a way to serialize and send metrics to an arbitrary backend.
    /// </summary>
    public interface IMetricHandler
    {
        /// <summary>
        /// Records the serialization of individual metrics so we can get statistics on
        /// bytes written and number of metrics written.
        /// </summary>
        /// <returns>
        /// An <see cref="IMetricBatch" /> used to add individual metrics.
        /// </returns>
        IMetricBatch BeginBatch();

        /// <summary>
        /// Serializes metadata about metrics.
        /// </summary>
        void SerializeMetadata(IEnumerable<MetaData> metadata);

        /// <summary>
        /// Serializes a metric.
        /// </summary>
        void SerializeMetric(in MetricReading reading);

        /// <summary>
        /// Flushes metrics to the underlying endpoint.
        /// </summary>
        /// <param name="delayBetweenRetries">
        /// <see cref="TimeSpan" /> between retries.
        /// </param>
        /// <param name="maxRetries">
        /// Maximum number of retries before we fail.
        /// </param>
        /// <param name="afterSend">
        /// Handler used to record statistics about the operation.
        /// </param>
        /// <param name="exceptionHandler">
        /// Handler used when to record exception information.
        /// </param>
        ValueTask FlushAsync(TimeSpan delayBetweenRetries, int maxRetries, Action<AfterSendInfo> afterSend, Action<Exception> exceptionHandler);

        /// <summary>
        /// Cleans-up any resources associated with this handler.
        /// </summary>
        ValueTask DisposeAsync();
    }
}
