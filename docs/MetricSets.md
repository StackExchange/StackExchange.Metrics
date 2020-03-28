# Metric Sets

Metric sets are pre-packaged sets of metrics that are useful across different applications. These can be added to a `MetricsCollector` by specifying in the `IEnumerable<IMetricSet>` exposed at `MetricsCollectorOptions.Sets`.

## Built-in sets

### ProcessMetricSet

Provides basic metrics for a .NET application. This set contains the following metrics:

 - `dotnet.cpu.processortime` - total processor time in seconds
 - `dotnet.cpu.threads` - threads for the process
 - `dotnet.mem.virtual` - virtual memory for the process
 - `dotnet.mem.paged` - paged memory for the process

### GarbageCollectionMetricSet (.NET Full Framework)

Provides metrics about the garbage collector (GC). This set contains the following metrics:

 - `dotnet.mem.collections.gen0` - number of gen-0 collections
 - `dotnet.mem.collections.gen1` - number of gen-1 collections
 - `dotnet.mem.collections.gen2` - number of gen-2 collections

### RuntimeMetricSet (.NET Core)

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

### AspNetMetricSet (.NET Core)

 - `dotnet.kestrel.requests.per_sec` - requests per second
 - `dotnet.kestrel.requests.total` - total requests
 - `dotnet.kestrel.requests.current` - current requests
 - `dotnet.kestrel.requests.failed` - failed requests

## Custom Metric Sets

Custom metric sets can be defined by implementing the `IMetricSet` interface. Implementors need to implement two methods:

 - `Initialize` - this method is passed an `IMetricsCollector` and should be used to create metrics that the set defines. It can also be used to hook up long-lived metrics monitoring checks
 - `Snapshot` - this method can be optionally implemented and is executed everytime the collector snapshots metrics for reporting

 Once defined `IMetricSet` implementations should be added to the `MetricsCollectorOptions` that is passed to the `MetricsCollector`.