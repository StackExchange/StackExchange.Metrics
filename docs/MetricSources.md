# Metric Sources

Metric sources are a way to consume metrics and their readings. These can be added to a `MetricsCollector` by specifying in the `IEnumerable<MetricSource>` exposed at `MetricsCollectorOptions.Sources`.

## Built-in sources

We provide some built-in metric sources that can report basic metrics within an application. Built-in sources can be added to a collector using the `MetricsCollectorOptions.Sources` property or by calling `AddDefaultSources` on the `IMetricsCollectorBuilder` exposed by `AddMetricsCollector` on an `IServiceCollection`.

### ProcessMetricSource

Provides basic metrics for a .NET application. This set contains the following metrics:

 - `dotnet.cpu.processortime` - total processor time in seconds
 - `dotnet.cpu.threads` - threads for the process
 - `dotnet.mem.virtual` - virtual memory for the process
 - `dotnet.mem.paged` - paged memory for the process

### GarbageCollectionMetricSource (.NET Full Framework only)

Provides metrics about the garbage collector (GC). This set contains the following metrics:

 - `dotnet.mem.collections.gen0` - number of gen-0 collections
 - `dotnet.mem.collections.gen1` - number of gen-1 collections
 - `dotnet.mem.collections.gen2` - number of gen-2 collections

### RuntimeMetricSource (.NET Core only)

Provides .NET Core runtime metrics which includes:

 - `dotnet.cpu.usage` - % CPU usage
 - `dotnet.mem.working_set` - working set of the process in bytes
 - `dotnet.mem.size.heap` - total number of bytes across all heaps
 - `dotnet.mem.size.gen0` - total number of bytes in gen-0
 - `dotnet.mem.size.gen1` - total number of bytes in gen-1
 - `dotnet.mem.size.gen2` - total number of bytes in gen-2
 - `dotnet.mem.size.loh` - total number of bytes in the LOH
 - `dotnet.mem.collections.gen0` - number of gen-0 collections
 - `dotnet.mem.collections.gen1` - number of gen-1 collections
 - `dotnet.mem.collections.gen2` - number of gen-2 collections
 - `dotnet.threadpool.count` - number of threads in the threadpool
 - `dotnet.threadpool.queue_length` - number of work items queued to the threadpool
 - `dotnet.timers.count` - number of active timers

 Note: The `dotnet.mem.` generation counters are only updated when the garbage collector runs.
 Until it runs, these counters will be zero and if allocations are low, these counters will be infrequently updated.

### AspNetMetricSource (.NET Core only)

 - `dotnet.kestrel.requests.per_sec` - requests per second
 - `dotnet.kestrel.requests.total` - total requests
 - `dotnet.kestrel.requests.current` - current requests
 - `dotnet.kestrel.requests.failed` - failed requests

## Custom Metric Sources

Custom metric sets can be defined by instantiating `MetricSource` and calling the `Add*` methods or by deriving from `MetricSource`.

Typically an application will define an `AppMetricSource` as follows:

```cs
public class AppMetricSource : MetricSource
{
    public Counter MyCounter { get; }
    public SamplingGauge<string, HttpStatusCode> MyGaugeWithTags { get; }
    
    public AppMetricSource(MetricSourceOptions options) : base(options)
    {
        MyCounter = AddCounter("my_counter", "units", "description");
        MyGaugeWithTags = AddCounter("my_gauge", "units", "description", new MetricTag<string>("route"), new MetricTag<HttpStatusCode>("status_code"));
    }
}
```

Custom metric sources can be configured using `MetricsCollectorOptions.Sources` in .NET full framework or the `AddSource` method on the `IMetricsCollectorBuilder` exposed by `AddMetricsCollector` on an `IServiceCollection`.