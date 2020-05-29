using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using StackExchange.Metrics.Infrastructure;
using StackExchange.Metrics.Metrics;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Represents a source of metrics for a <see cref="IMetricsCollector" />.
    /// </summary>
    public partial class MetricSource : IMetricReadingWriter, IMetricMetadataProvider
    {
        private ImmutableArray<IMetricReadingWriter> _metrics = ImmutableArray<IMetricReadingWriter>.Empty;

        /// <summary>
        /// Initializes a <see cref="MetricSource"/>.
        /// </summary>
        /// <param name="options">
        /// <see cref="MetricSourceOptions" /> representing the options to use when creating metrics in this source.
        /// </param>
        public MetricSource(MetricSourceOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Gets <see cref="MetricSourceOptions"/> that should be used when creating metrics in this source.
        /// </summary>
        protected MetricSourceOptions Options { get; }

        /// <summary>
        /// Called when an <see cref="IMetricsCollector"/> is starting in order to attach
        /// any event handlers, etc. the source may be attached to.
        /// </summary>
        /// <param name="collector">
        /// An <see cref="IMetricsCollector"/>.
        /// </param>
        /// <remarks>
        /// This method is typically overridden in derived implementations in order
        /// to attach to the <see cref="IMetricsCollector.BeforeSerialization"/> event
        /// so that a metric source can perform pre-snapshotting processing.
        /// </remarks>
        public virtual void Attach(IMetricsCollector collector)
        {
        }

        /// <summary>
        /// Called when an <see cref="IMetricsCollector"/> is stopping in order to detach
        /// any event handlers, etc. the source may be attached to.
        /// </summary>
        /// <param name="collector">
        /// An <see cref="IMetricsCollector"/>.
        /// </param>
        public virtual void Detach(IMetricsCollector collector)
        {
        }

        /// <inheritdoc/>
        public void WriteReadings(IMetricReadingBatch batch, DateTime timestamp)
        {
            var metrics = _metrics;
            for (var i = 0; i < metrics.Length; i++)
            {
                metrics[i].WriteReadings(batch, timestamp);
            }
        }

        /// <inheritdoc/>
        public IEnumerable<Metadata> GetMetadata()
        {
            var metrics = _metrics;
            if (metrics.IsDefaultOrEmpty)
            {
                yield break;
            }

            foreach (var metadataProvider in metrics.OfType<IMetricMetadataProvider>())
            {
                foreach (var metadata in metadataProvider.GetMetadata())
                {
                    yield return metadata;
                }
            }
        }

        /// <summary>
        /// Adds a new metric to the source.
        /// </summary>
        /// <typeparam name="TMetric">
        /// Type of metric to add.
        /// </typeparam>
        /// <param name="metric">
        /// Instance of a metric.
        /// </param>
        public TMetric Add<TMetric>(TMetric metric) where TMetric : MetricBase
        {
            _metrics = _metrics.Add(metric);
            return metric;
        }

        /// <summary>
        /// Adds a new metric to the source.
        /// </summary>
        /// <typeparam name="TTaggedMetric">
        /// Type of tagged metric to add.
        /// </typeparam>
        /// <typeparam name="TMetric">
        /// Type of metric.
        /// </typeparam>
        /// <param name="metric">
        /// Instance of a metric.
        /// </param>
        public TTaggedMetric Add<TTaggedMetric, TMetric>(TTaggedMetric metric) where TTaggedMetric : TaggedMetricFactory<TMetric> where TMetric : MetricBase
        {
            _metrics = _metrics.Add(metric);
            return metric;
        }

        /// <summary>
        /// Creates a new <see cref="AggregateGauge" /> using <see cref="GaugeAggregator.Default"/> aggregators and adds it to this source.
        /// </summary>
        public virtual AggregateGauge AddAggregateGauge(string name, string unit, string description) => Add(new AggregateGauge(GaugeAggregator.Default, name, unit, description, Options));

        /// <summary>
        /// Creates a new <see cref="AggregateGauge" /> using the specified aggregators and adds it to this source.
        /// </summary>
        public virtual AggregateGauge AddAggregateGauge(IEnumerable<GaugeAggregator> aggregators, string name, string unit, string description) => Add(new AggregateGauge(aggregators, name, unit, description, Options));

        /// <summary>
        /// Creates a new <see cref="Counter" /> and adds it to this source.
        /// </summary>
        public virtual Counter AddCounter(string name, string unit, string description) => Add(new Counter(name, unit, description, Options));

        /// <summary>
        /// Creates a new <see cref="CumulativeCounter" /> and adds it to this source.
        /// </summary>
        public virtual CumulativeCounter AddCumulativeCounter(string name, string unit, string description) => Add(new CumulativeCounter(name, unit, description, Options));

        /// <summary>
        /// Creates a new <see cref="EventGauge" /> and adds it to this source.
        /// </summary>
        public virtual EventGauge AddEventGauge(string name, string unit, string description) => Add(new EventGauge(name, unit, description, Options));

        /// <summary>
        /// Creates a new <see cref="SamplingGauge" /> and adds it to this source.
        /// </summary>
        public virtual SamplingGauge AddSamplingGauge(string name, string unit, string description) => Add(new SamplingGauge(name, unit, description, Options));

        /// <summary>
        /// Creates a new <see cref="SnapshotCounter" /> and adds it to this source.
        /// </summary>
        public virtual SnapshotCounter AddSnapshotCounter(Func<long?> getCountFunc, string name, string unit, string description) => Add(new SnapshotCounter(getCountFunc, name, unit, description, Options));

        /// <summary>
        /// Creates a new <see cref="SnapshotGauge" /> and adds it to this source.
        /// </summary>
        public virtual SnapshotGauge AddSnapshotGauge(Func<double?> getValueFunc, string name, string unit, string description) => Add(new SnapshotGauge(getValueFunc, name, unit, description, Options));
    }
}

