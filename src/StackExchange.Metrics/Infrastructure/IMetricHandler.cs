using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StackExchange.Metrics.Infrastructure
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
        /// An <see cref="IMetricReadingBatch" /> used to add individual readings.
        /// </returns>
        IMetricReadingBatch BeginBatch();

        /// <summary>
        /// Serializes metadata about metrics.
        /// </summary>
        void SerializeMetadata(IEnumerable<Metadata> metadata);

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
        void Dispose();
    }
}
