using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Metrics.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Metrics
{
    /// <summary>
    /// The primary class for collecting metric readings and dispatching them to handlers.
    /// </summary>
    public partial class MetricsCollector : IMetricsCollector, IHostedService
    {
        private readonly ImmutableArray<MetricEndpoint> _endpoints;
        private readonly ImmutableArray<MetricSource> _sources;
        private readonly IMetricReadingBatch[] _batches;

        private bool _hasNewMetadata;
        private DateTime _lastMetadataFlushTime = DateTime.MinValue;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "Used to hold ref.")]
        private Task _flushTask;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "Used to hold ref.")]
        private Task _reportingTask;
        private CancellationTokenSource _shutdownTokenSource;

        /// <summary>
        /// If true, we will generate an exception every time posting to the a metrics endpoint fails with a server error (response code 5xx).
        /// </summary>
        public bool ThrowOnPostFail { get; set; }
        /// <summary>
        /// If true, we will generate an exception when the metric queue is full. This would most commonly be caused by an extended outage of the
        /// a metric handler. It is an indication that data is likely being lost.
        /// </summary>
        public bool ThrowOnQueueFull { get; set; }
        /// <summary>
        /// The length of time between metric reports (snapshots).
        /// </summary>
        public TimeSpan ReportingInterval { get; }
        /// <summary>
        /// The length of time between flush operations to an endpoint.
        /// </summary>
        public TimeSpan FlushInterval { get; }
        /// <summary>
        /// Number of times to retry a flush operation before giving up.
        /// </summary>
        public int RetryCount { get; }
        /// <summary>
        /// The length of time to wait before retrying a failed flush operation to an endpoint.
        /// </summary>
        public TimeSpan RetryInterval { get; }
        /// <summary>
        /// Exceptions which occur on a background thread within the collector will be passed to this delegate.
        /// </summary>
        public Action<Exception> ExceptionHandler { get; }

        /// <summary>
        /// An event called immediately before metric readings are collected.
        /// </summary>
        public event Action BeforeSerialization;
        /// <summary>
        /// An event called immediately after metrics readings have been collected. It includes an argument with post-serialization information.
        /// </summary>
        public event Action<AfterSerializationInfo> AfterSerialization;
        /// <summary>
        /// An event called immediately after metrics are posted to a metric handler. It includes an argument with information about the POST.
        /// </summary>
        public event Action<AfterSendInfo> AfterSend;

        /// <summary>
        /// True if <see cref="Stop"/> has been called on this collector.
        /// </summary>
        public bool ShutdownCalled => _shutdownTokenSource?.IsCancellationRequested ?? true;

        /// <summary>
        /// Enumerable of all endpoints managed by this collector.
        /// </summary>
        public IEnumerable<MetricEndpoint> Endpoints => _endpoints.AsEnumerable();

        /// <summary>
        /// Enumerable of all sources consumed by this collector.
        /// </summary>
        public IEnumerable<MetricSource> Sources => _sources.AsEnumerable();

        /// <summary>
        /// Instantiates a new collector. You should typically only instantiate one collector for the lifetime of your
        /// application. It will manage the serialization of metrics and sending data to metric handlers.
        /// </summary>
        /// <param name="options">
        /// <see cref="MetricsCollectorOptions" /> representing the options to use for this collector.
        /// </param>
        [ActivatorUtilitiesConstructor]
        public MetricsCollector(IOptions<MetricsCollectorOptions> options) : this(options.Value)
        {
        }

        /// <summary>
        /// Instantiates a new collector. You should typically only instantiate one collector for the lifetime of your
        /// application. It will manage the serialization of metrics and sending data to metric handlers.
        /// </summary>
        /// <param name="options">
        /// <see cref="MetricsCollectorOptions" /> representing the options to use for this collector.
        /// </param>
        public MetricsCollector(MetricsCollectorOptions options)
        {
            ExceptionHandler = options.ExceptionHandler ?? (_ => { });
            ThrowOnPostFail = options.ThrowOnPostFail;
            ThrowOnQueueFull = options.ThrowOnQueueFull;
            ReportingInterval = options.SnapshotInterval;
            FlushInterval = options.FlushInterval;
            RetryInterval = options.RetryInterval;
            RetryCount = options.RetryCount;

            _endpoints = options.Endpoints?.ToImmutableArray() ?? ImmutableArray<MetricEndpoint>.Empty;
            _sources = options.Sources?.ToImmutableArray() ?? ImmutableArray<MetricSource>.Empty;
            _batches = _endpoints.IsDefaultOrEmpty ? Array.Empty<IMetricReadingBatch>() : new IMetricReadingBatch[_endpoints.Length];
        }

        /// <summary>
        /// Starts the collector.
        /// </summary>
        /// <remarks>
        /// This operation starts the snapshot and flush background tasks and attaches the collector
        /// to all of its metric sources.
        /// </remarks>
        public void Start()
        {
            // attach to all metric sources
            foreach (var source in _sources)
            {
                source.Attach(this);
            }

            _shutdownTokenSource = new CancellationTokenSource();

            // start background threads for flushing and snapshotting
            _flushTask = Task.Run(
                async () =>
                {
                    while (!_shutdownTokenSource.IsCancellationRequested)
                    {
                        await Task.Delay(FlushInterval);

                        try
                        {
                            await FlushAsync();
                        }
                        catch (Exception ex)
                        {
                            SendExceptionToHandler(ex);
                        }
                    }
                });

            _reportingTask = Task.Run(
                async () =>
                {
                    while (!_shutdownTokenSource.IsCancellationRequested)
                    {
                        await Task.Delay(ReportingInterval);

                        try
                        {
                            await SnapshotAsync();
                        }
                        catch (Exception ex)
                        {
                            SendExceptionToHandler(ex);
                        }
                    }
                });
        }

        /// <summary>
        /// Stops the collector.
        /// </summary>
        /// <remarks>
        /// This operation cancels the snapshot and flush background tasks, detaches the collector
        /// from all of its metric sources and waits for background tasks to complete.
        /// </remarks>
        public void Stop()
        {
            if (_shutdownTokenSource == null)
            {
                throw new InvalidOperationException("Collector was not started");
            }

            Debug.WriteLine("StackExchange.Metrics: Shutting down MetricsCollector.");

            // notify background tasks to stop
            _shutdownTokenSource.Cancel();

            // clean-up all metric endpoints
            foreach (var endpoint in _endpoints)
            {
                endpoint.Handler.Dispose();
            }

            // and detach from all sources
            foreach (var source in _sources)
            {
                source.Detach(this);
            }
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            Start();
            return Task.CompletedTask;
        }

        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            Stop();
            return Task.CompletedTask;
        }

        private Task SnapshotAsync()
        {
            try
            {
                var beforeSerialization = BeforeSerialization;
                if (beforeSerialization?.GetInvocationList().Length > 0)
                    beforeSerialization();

                var timestamp = DateTime.UtcNow;
                var sw = Stopwatch.StartNew();
                var batch = new CompositeBatch(_batches);
                for (var i = 0; i < _endpoints.Length; i++)
                {
                    _batches[i] = _endpoints[i].Handler.BeginBatch();
                }

                foreach (IMetricReadingWriter source in _sources)
                {
                    source.WriteReadings(batch, timestamp);
                }

                if (_hasNewMetadata || DateTime.UtcNow - _lastMetadataFlushTime >= TimeSpan.FromDays(1))
                {
                    using (var metadata = _sources.GetMetadata())
                    {
                        if (metadata.Count > 0)
                        {
                            for (var i = 0; i < _endpoints.Length; i++)
                            {
                                SerializeMetadata(_endpoints[i], metadata);
                            }
                            
                        }
                    }
                    _hasNewMetadata = false;
                }
                sw.Stop();

                var stats = batch.GetStatistics();

                AfterSerialization?.Invoke(
                    new AfterSerializationInfo
                    {
                        BytesWritten = stats.BytesWritten,
                        Count = stats.MetricsWritten,
                        Duration = sw.Elapsed,
                        StartTime = timestamp
                    });
            }
            catch (Exception ex)
            {
                SendExceptionToHandler(ex);
            }

            return Task.CompletedTask;
        }

        private async Task FlushAsync()
        {
            if (_endpoints.Length == 0)
            {
                Debug.WriteLine("StackExchange.Metrics: No endpoints. Dropping data.");
                return;
            }

            foreach (var endpoint in _endpoints)
            {
                Debug.WriteLine($"StackExchange.Metrics: Flushing metrics for {endpoint.Name}");

                try
                {
                    await endpoint.Handler.FlushAsync(
                        RetryInterval,
                        RetryCount,
                        // Use Task.Run here to invoke the event listeners asynchronously.
                        // We're inside a lock, so calling the listeners synchronously would put us at risk of a deadlock.
                        info => Task.Run(
                            () =>
                            {
                                info.Endpoint = endpoint.Name;
                                try
                                {
                                    AfterSend?.Invoke(info);
                                }
                                catch (Exception ex)
                                {
                                    SendExceptionToHandler(ex);
                                }
                            }
                        ),
                        ex => SendExceptionToHandler(ex)
                    );
                }
                catch (Exception ex)
                {
                    // this will be hit if a sending operation repeatedly fails
                    SendExceptionToHandler(ex);
                }
            }
        }

        private void SerializeMetadata(MetricEndpoint endpoint, IEnumerable<Metadata> metadata)
        {
            Debug.WriteLine("StackExchange.Metrics: Serializing metadata.");
            endpoint.Handler.SerializeMetadata(metadata);
            _lastMetadataFlushTime = DateTime.UtcNow;
            Debug.WriteLine("StackExchange.Metrics: Serialized metadata.");
        }

        private void SendExceptionToHandler(Exception ex)
        {
            if (!ShouldSendException(ex))
                return;

            try
            {
                ExceptionHandler(ex);
            }
#pragma warning disable RCS1075 // Avoid empty catch clause that catches System.Exception.
            catch (Exception) { } // there's nothing else we can do if the user-supplied exception handler itself throws an exception
#pragma warning restore RCS1075 // Avoid empty catch clause that catches System.Exception.
        }

        private bool ShouldSendException(Exception ex)
        {
            if (ex is MetricPostException post)
            {
                if (post.SkipExceptionHandler)
                {
                    return false;
                }

                if (ThrowOnPostFail)
                    return true;

                return false;
            }

            if (ex is MetricQueueFullException)
                return ThrowOnQueueFull;

            return true;
        }

        private class CompositeBatch : IMetricReadingBatch
        {
            private readonly IMetricReadingBatch[] _children;

            public CompositeBatch(IMetricReadingBatch[] children)
            {
                _children = children;
            }

            public long BytesWritten => 0;
            public long MetricsWritten => 0;

            public (long BytesWritten, long MetricsWritten) GetStatistics()
            {
                long bytesWritten = 0L, metricsWritten = 0L;
                for (var i = 0; i < _children.Length; i++)
                {
                    bytesWritten += _children[i].BytesWritten;
                    metricsWritten += _children[i].MetricsWritten;
                }
                return (bytesWritten, metricsWritten);
            }

            public void Add(in MetricReading reading)
            {
                for (var i = 0; i < _children.Length; i++)
                {
                    _children[i].Add(reading);
                }
            }
        }
    }

    /// <summary>
    /// Information about a metrics serialization pass.
    /// </summary>
    public class AfterSerializationInfo
    {
        /// <summary>
        /// The number of data points serialized. The could be less than or greater than the number of metrics managed by the collector.
        /// </summary>
        public long Count { get; internal set; }
        /// <summary>
        /// The number of bytes written to payload(s).
        /// </summary>
        public long BytesWritten { get; internal set; }
        /// <summary>
        /// The duration of the serialization pass.
        /// </summary>
        public TimeSpan Duration { get; internal set; }
        /// <summary>
        /// The time serialization started.
        /// </summary>
        public DateTime StartTime { get; internal set; }
    }

    /// <summary>
    /// Information about a send to a metrics endpoint.
    /// </summary>
    public class AfterSendInfo
    {
        /// <summary>
        /// Endpoint that we sent data to.
        /// </summary>
        public string Endpoint { get; internal set; }
        /// <summary>
        /// Gets a <see cref="PayloadType" /> indicating the type of payload that was flushed.
        /// </summary>
        public PayloadType PayloadType { get; internal set; }
        /// <summary>
        /// The number of bytes in the payload. This does not include HTTP header bytes.
        /// </summary>
        public long BytesWritten { get; internal set; }
        /// <summary>
        /// The duration of the POST.
        /// </summary>
        public TimeSpan Duration { get; internal set; }
        /// <summary>
        /// True if the POST was successful. If false, <see cref="Exception"/> will be non-null.
        /// </summary>
        public bool Successful => Exception == null;
        /// <summary>
        /// Information about a POST failure, if applicable. Otherwise, null.
        /// </summary>
        public Exception Exception { get; internal set; }
        /// <summary>
        /// The time the POST was initiated.
        /// </summary>
        public DateTime StartTime { get; }

        internal AfterSendInfo()
        {
            StartTime = DateTime.UtcNow;
        }
    }
}
