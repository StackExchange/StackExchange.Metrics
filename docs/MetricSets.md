# Metric Sets

Metric sets are pre-packaged sets of metrics that are useful across different applications. These can be added to a `MetricsCollector` by specifying in the `IEnumerable<IMetricSet>` exposed at `MetricsCollectorOptions.Sets`.

## Built-in sets

### ProcessMetricSet

Provides basic metrics for a .NET application. This set contains the following metrics:

 - `cpu.processortime` - total processor time in seconds
 - `cpu.threads` - threads for the process
 - `mem.virtual` - virtual memory for the process
 - `mem.paged` - paged memory for the process

### GarbageCollectionMetricSet (.NET Full Framework)

Provides metrics about the garbage collector (GC). This set contains the following metrics:

 - `mem.collections_gen0` - number of gen-0 collections
 - `mem.collections_gen1` - number of gen-1 collections
 - `mem.collections_gen2` - number of gen-2 collections

### RuntimeMetricSet (.NET Core)

Provides .NET Core runtime metrics which includes:

 - `cpu.usage` - % CPU usage
 - `mem.working_set` - working set of the process in bytes
 - `mem.size_heap` - total number of bytes across all heaps
 - `mem.size_gen0` - total number of bytes in gen-0
 - `mem.size_gen1` - total number of bytes in gen-1
 - `mem.size_gen2` - total number of bytes in gen-2
 - `mem.size_loh` - total number of bytes in the LOH
 - `mem.collection_gen0` - number of gen-0 collections
 - `mem.collection_gen1` - number of gen-1 collections
 - `mem.collection_gen2` - number of gen-2 collections
 - `threadpool.count` - number of threads in the threadpool
 - `threadpool.queue_length` - number of work items queued to the threadpool
 - `timers.count` - number of active timers

 Note: The `mem.` generation counters are only updated when the garbage collector runs.
 Until it runs, these counters will be zero and if allocations are low, these counters will be infrequently updated.

### AspNetMetricSet (.NET Core)

 - `kestrel.requests.pre_sec` - requests per second
 - `kestrel.requests.total` - total requests
 - `kestrel.requests.current` - current requests
 - `kestrel.requests.failed` - failed requests

## Custom Metric Sets

Custom metric sets can be defined by implementing the `IMetricSet` interface. Implementors need to implement two methods:

 - `Initialize` - this method is passed an `IMetricsCollector` and should be used to create metrics that the set defines. It can also be used to hook up long-lived metrics monitoring checks
 - `Snapshot` - this method can be optionally implemented and is executed everytime the collector snapshots metrics for reporting

 Once defined `IMetricSet` implementations should be added to the `MetricsCollectorOptions` that is passed to the `MetricsCollector`.