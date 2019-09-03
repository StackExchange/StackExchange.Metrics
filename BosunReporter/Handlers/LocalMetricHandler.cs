using BosunReporter.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BosunReporter.Handlers
{
    /// <summary>
    /// Represents metadata about a metric.
    /// </summary>
    public readonly struct LocalMetricMetadata
    {
        /// <summary>
        /// Constructs a new <see cref="LocalMetricMetadata" /> instance.
        /// </summary>
        /// <param name="metric">
        /// Name of a metric.
        /// </param>
        /// <param name="type">
        /// Type of the metric.
        /// </param>
        /// <param name="description">
        /// Descriptive text for the metric.
        /// </param>
        /// <param name="unit">
        /// Unit of the metric.
        /// </param>
        public LocalMetricMetadata(string metric, string type, string description, string unit)
        {
            Metric = metric;
            Type = type;
            Description = description;
            Unit = unit;
        }

        /// <summary>
        /// Gets the name of the metric.
        /// </summary>
        public string Metric { get; }
        /// <summary>
        /// Gets the type of a metric.
        /// </summary>
        public string Type { get; }
        /// <summary>
        /// Gets descriptive text for a metric.
        /// </summary>
        public string Description { get; }
        /// <summary>
        /// Gets the unit of a metric.
        /// </summary>
        public string Unit { get; }
    }

    /// <summary>
    /// An implementation of <see cref="IMetricHandler" /> that stores metrics locally
    /// so that they can be pulled via an API.
    /// </summary>
    public class LocalMetricHandler : IMetricHandler
    {
        readonly object _readingLock;
        readonly object _metadataLock;
        readonly Dictionary<string, MetricReading> _readings;
        readonly List<LocalMetricMetadata> _metadata;

        /// <summary>
        /// Constructs a new <see cref="LocalMetricMetadata" />.
        /// </summary>
        public LocalMetricHandler()
        {
            _readingLock = new object();
            _metadataLock = new object();
            _readings = new Dictionary<string, MetricReading>(StringComparer.OrdinalIgnoreCase);
            _metadata = new List<LocalMetricMetadata>();
        }

        /// <summary>
        /// Returns all the metadata that has been recorded.
        /// </summary>
        public IEnumerable<LocalMetricMetadata> GetMetadata()
        {
            lock (_metadataLock)
            {
                return _metadata.ToList();
            }
        }

        /// <summary>
        /// Returns a current snapshot of all metrics.
        /// </summary>
        /// <param name="reset">
        /// Indicates whether to reset the readings once they've been read.
        /// </param>
        public IEnumerable<MetricReading> GetReadings(bool reset = false)
        {
            lock (_readingLock)
            {
                var readings = _readings.Values.ToList();
                if (reset)
                {
                    _readings.Clear();
                }

                return readings;
            }
        }

        /// <inheritdoc />
        public IMetricBatch BeginBatch() => new Batch(this);

        /// <inheritdoc />
        public ValueTask FlushAsync(TimeSpan delayBetweenRetries, int maxRetries, Action<AfterSendInfo> afterSend, Action<Exception> exceptionHandler)
        {
            afterSend?.Invoke(
                new AfterSendInfo
                {
                    Duration = TimeSpan.Zero,
                    BytesWritten = 0
                });

            return default;
        }

        /// <inheritdoc />
        public void SerializeMetadata(IEnumerable<MetaData> metadata)
        {
            lock (_metadataLock)
            {
                _metadata.Clear();
                _metadata.AddRange(
                    metadata
                        .GroupBy(x => x.Metric)
                        .Select(
                            g => new LocalMetricMetadata(
                                metric: g.Key,
                                type: g.Where(x => x.Name == MetadataNames.Rate).Select(x => x.Value).FirstOrDefault(),
                                description: g.Where(x => x.Name == MetadataNames.Description).Select(x => x.Value).FirstOrDefault(),
                                unit: g.Where(x => x.Name == MetadataNames.Unit).Select(x => x.Value).FirstOrDefault()
                            )
                        )
                );
            }
        }

        /// <inheritdoc />
        public void SerializeMetric(in MetricReading reading)
        {
            lock (_readingLock)
            {
                var isCounter = reading.Type == MetricType.Counter || reading.Type == MetricType.CumulativeCounter;
                if (isCounter && _readings.TryGetValue(reading.NameWithSuffix, out var existingReading))
                {
                    _readings[reading.NameWithSuffix] = existingReading.Update(reading.Value, reading.Timestamp);
                }
                else
                {
                    _readings[reading.NameWithSuffix] = reading;
                }
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => default;

        private class Batch : IMetricBatch
        {
            private readonly LocalMetricHandler _handler;

            public Batch(LocalMetricHandler handler)
            {
                _handler = handler;
            }

            public long BytesWritten { get; private set; }
            public long MetricsWritten { get; private set; }

            /// <inheritdoc />
            public void SerializeMetric(in MetricReading reading)
            {
                _handler.SerializeMetric(reading);
                MetricsWritten++;
                BytesWritten = 0;
            }

            public void Dispose()
            {
            }
        }
    }
}
