#if NETCOREAPP
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Implements <see cref="IDiagnosticsCollector" /> by hooking into the
    /// .NET Core diagnostics client.
    /// </summary>
    internal class DiagnosticsCollector : IHostedService, IDiagnosticsCollector
    {
        private readonly ILogger<DiagnosticsCollector> _logger;

        private ImmutableDictionary<(string, string), Action<long>> _counterCallbacks = ImmutableDictionary.Create<(string, string), Action<long>>();
        private ImmutableDictionary<(string, string), Action<double>> _gaugeCallbacks = ImmutableDictionary.Create<(string, string), Action<double>>();
        private ImmutableArray<EventPipeProvider> _eventSources = ImmutableArray<EventPipeProvider>.Empty;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _reportingTask;

        public DiagnosticsCollector(ILogger<DiagnosticsCollector> logger)
        {
            _logger = logger;
        }

        public void AddSource(EventPipeProvider source)
        {
            _eventSources = _eventSources.Add(source);
        }

        public void AddCounterCallback(string provider, string name, Action<long> action)
        {
            _counterCallbacks = _counterCallbacks.SetItem((provider, name), action);
        }

        public void AddGaugeCallback(string provider, string name, Action<double> action)
        {
            _gaugeCallbacks = _gaugeCallbacks.SetItem((provider, name), action);
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _reportingTask = Task.Run(
                async () =>
                {
                    int processId;
                    using (var process = Process.GetCurrentProcess())
                    {
                        processId = process.Id;
                    }

                    while (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        if (_eventSources.IsDefaultOrEmpty)
                        {
                            await Task.Delay(1000);
                            continue;
                        }

                        var client = new DiagnosticsClient(processId);
                        var session = default(EventPipeSession);
                        try
                        {
                            session = client.StartEventPipeSession(_eventSources);
                        }
                        catch (EndOfStreamException)
                        {
                            break;
                        }
                        // If the process has already exited, a ServerNotAvailableException will be thrown.
                        catch (ServerNotAvailableException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                _logger.LogDebug(0, ex, "Failed to start the event pipe session");
                            }

                            // We can't even start the session, wait until the process boots up again to start another metrics thread
                            break;
                        }

                        void StopSession()
                        {
                            try
                            {
                                session.Stop();
                            }
                            catch (EndOfStreamException)
                            {
                                // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
                            }
                            // We may time out if the process ended before we sent StopTracing command. We can just exit in that case.
                            catch (TimeoutException)
                            {
                            }
                            // On Unix platforms, we may actually get a PNSE since the pipe is gone with the process, and Runtime Client Library
                            // does not know how to distinguish a situation where there is no pipe to begin with, or where the process has exited
                            // before dotnet-counters and got rid of a pipe that once existed.
                            // Since we are catching this in StopMonitor() we know that the pipe once existed (otherwise the exception would've 
                            // been thrown in StartMonitor directly)
                            catch (PlatformNotSupportedException)
                            {
                            }
                            // If the process has already exited, a ServerNotAvailableException will be thrown.
                            // This can always race with tye shutting down and a process being restarted on exiting.
                            catch (ServerNotAvailableException)
                            {
                            }
                        }

                        using var _ = cancellationToken.Register(() => StopSession());

                        try
                        {
                            using (var source = new EventPipeEventSource(session.EventStream))
                            {
                                source.Dynamic.All += traceEvent =>
                                {
                                    try
                                    {
                                    // Metrics
                                    if (traceEvent.EventName.Equals("EventCounters"))
                                    {
                                            var payloadVal = (IDictionary<string, object>)traceEvent.PayloadValue(0);
                                            var eventPayload = (IDictionary<string, object>)payloadVal["Payload"];
                                            var providerName = traceEvent.ProviderName;
                                            var eventName = (string)eventPayload["Name"];
                                            var callbackKey = (providerName, eventName);
                                            if (eventPayload.TryGetValue("CounterType", out var type) && type is string counterType)
                                            {
                                                if (counterType == "Sum" && _counterCallbacks.TryGetValue(callbackKey, out var counterCallback))
                                                {
                                                    counterCallback((long)(double)eventPayload["Increment"]);
                                                }
                                                else if (_gaugeCallbacks.TryGetValue(callbackKey, out var gaugeCallback))
                                                {
                                                    gaugeCallback((double)eventPayload["Mean"]);
                                                }
                                            }
                                            else if (eventPayload.Count == 6)
                                            {
                                                if (_counterCallbacks.TryGetValue(callbackKey, out var counterCallback))
                                                {
                                                    counterCallback((long)(double)eventPayload["Increment"]);
                                                }
                                            }
                                            else if (_gaugeCallbacks.TryGetValue(callbackKey, out var gaugeCallback))
                                            {
                                                gaugeCallback((double)eventPayload["Mean"]);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error processing counter for {ProviderName}:{EventName}", traceEvent.ProviderName, traceEvent.EventName);
                                    }
                                };

                                source.Process();
                            }
                        }
                        catch (DiagnosticsClientException ex)
                        {
                            _logger.LogDebug(0, ex, "Failed to start the event pipe session");
                        }
                        catch (Exception)
                        {
                            // This fails if stop is called or if the process dies
                        }
                        finally
                        {
                            session?.Dispose();
                        }
                    }
                }
            );

            return Task.CompletedTask;
        }

        async Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            await _reportingTask;
        }
    }
}
#endif
