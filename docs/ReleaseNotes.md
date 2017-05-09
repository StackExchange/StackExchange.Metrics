# BosunReporter.NET Release Notes

## 4.0.0

#### Exception Handling

Many BosunReporter tasks run on background threads, where an uncaught exception will crash the process. BosunReporter previously allowed you to subscribe to an "OnBackgroundException" event to handle exceptions, but if you didn't, exceptions would be thrown and take down your process.

The exception handler is now required. Instead of an event, it's an `Action<Exception>` parameter on the MetricsCollector.

```csharp
var options = new BosunOptions { ... };
var collector = new MetricsCollector(options, ex => LogException(ex));
```

#### Minor Changes

-   An Access Token can be provided via `BosunOptions.AccessToken` or `BosunOptions.GetAccessToken`. If not null or empty, this will be added as an `X-Access-Token` header on all API requests.
-   `MetricGroup<T, TMetric>.PopulateFromEnum()` now has an optional `includeObsolete` parameter (defaults to true). If false, obsolete enum values will not be used to populated the metric group.
-   [AggregateGauge](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#aggregategauge):
    -   Is now abstract. You must inherit from it in order to use it.
    -   If a child class does not specify aggregators of its own (via `[GaugeAggregator]`), it will inherit its parent's aggregators. If a child class _does_ specify at least one aggregator, then it will _not_ inherit any of its parent's aggregators.
-   [SnapshotCounter](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#snapshotcounter) and [SnapshotGauge](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#snapshotgauge):
    -   No longer have default protected constructors
    -   The `Func<T>` argument is always required
    -   The `Func<T>` field is now private
    -   `GetValue` is now public
-   Removed all metric interfaces (IDoubleGauge, ILongGauge, IIntGauge, IDoubleCounter, ILongCounter, IIntCounter). They really weren't all that useful. You should generally know what you're recording on explicitly. Using an interface probably represents an anti-pattern. If you really need an interface, you can define your own interface and metric types which implement them.
-   `BosunPostException.StatusCode` is now nullable, since not all POST errors will have an HTTP status code returned.
-   Removed the [Jil](https://github.com/kevin-montrose/Jil) dependency. BosunReporter now only depends on the Framework (we should eventually migrate to .NET Standard).
-   Added XML documentation for all public types and methods

---

#### 3.0.1

- Stop metric serialization when the queue is full so that we only throw the queue-full exception once per serialization interval.
- `BosunOptions.ThrowOnPostFail` now only applies to 5xx response codes. So, if a 4xx is received, an exception will still be thrown since that probably indicates a problem with the library rather than Bosun being down.
- Increased the default `BosunOptions.MaxPendingPayloads` from 120 to 240.

## 3.0.0

There are several changes in version 3.0, including some new features and minor breaking changes.

#### Metric Batching Options

The way BosunReporter batches metrics to Bosun has changed substantially. Instead of having a batch size specified in metrics, the maximum batch size is now specified in bytes, which gives you better control over network traffic. However, this means that some [BosunOptions](https://github.com/bretcope/BosunReporter.NET/blob/master/BosunReporter/BosunOptions.cs) had to change.

- `BatchSize` (unit: metrics), has been replaced with `MaxPayloadSize` (unit: bytes). The default is 8000 bytes.
- `MaxQueueLength` (unit: metrics) has been replaced with `MaxPendingPayloads` (unit: payloads) which is the maximum number of payloads (each with a max size of `MaxPayloadSize`) to queue for sending. Payloads are re-added to the queue when sending to Bosun fails, unless the queue is full. The default is 120 payloads.

#### Ignoring Default Tags

`[IgnoreDefaultBosunTags]`, which could be applied to a metric class in order to not inherit the default tags, has been removed and replaced by `[ExcludeDefaultTags]`.

The new attribute can be used to either ignore all, or only some, of the default tags.

```csharp
[ExcludeDefaultTags]
public class Metric1 : BosunMetric
{
    // all default tags are excluded
}

[ExcludeDefaultTags("host", "tier")]
public class Metric2 : BosunMetric
{
    // default tags "host" and "tier" are specifically excluded
}
```

> Keep in mind that _all_ metrics (except external counters) must have at least one tag. So, if you exclude all default tags, you will need to add at least one of your own.

There is also a complementary attribute `[RestoreDefaultTags]` which can be used to restore some, or all, of the default tags. This can be useful if you inherit from a class which excludes default tags.

```csharp
[RestoreDefaultTags("host")]
public class Metric3 : Metric1
{
    // "host" tag will now be included, even though Metric1 excludes default tags
}
```

#### External Counters

>  This feature requires you to be using [tsdbrelay](https://github.com/bosun-monitor/bosun/tree/master/cmd/tsdbrelay) as an intermediary between your app and Bosun. You'll need to run tsdbrelay with `-redis=REDIS_SERVER_NAME` and setup an [scollector](https://github.com/bosun-monitor/bosun/tree/master/cmd/scollector) instance to scrape it with:
>
>  ```
>  [[RedisCounters]] 
>  Server = "localhost:6379" 
>  Database = 2
>  ```

External counters are intended to solve the problem of counting low-volume events.

The nature of a low-volume counter is that its per-second rate is going to be zero most of the time. For example:

![](https://i.stack.imgur.com/qD8Ki.png)

If you could simply see the start and end values for a given time interval, you would have a better sense of how frequent the events are. But, unfortunately, a normal Bosun counter resets every time the application restarts, so you end up with a graph that might look something like this when viewed as a gauge:

![](https://i.stack.imgur.com/wwGrO.png)

To solve this problem, external counters are persistent (the value doesn't reset every time the app restarts). Tsdbrelay stores the value of the counter in Redis, and BosunReporter sends it increments when an event happens. Tsdbrelay then periodically reports the metric to Bosun.

This means that when you graph the metric as a gauge, it will always be going up, and you can easily see start and end values for any time interval.

Remember, __these are for LOW VOLUME__ events. If you expect more than one event per minute across all instances of your app, you should use a normal counter.

You can enable/disable sending external counter increments using `BosunOptions.EnableExternalCounters` during initialization, or by changing `MetricsCollector.EnableExternalCounters` at runtime.

##### Usage

The usage of `ExternalCounter` is exactly the same as `Counter` except that you can only increment by 1.

```csharp
var counter = collector.CreateMetric<ExternalCounter>("ext_counter", "units", "description");
counter.Increment();
```

You can also inherit from `ExternalCounter` in order to add tags (like any other metric type).

>  tsdbrelay will automatically add the "host" tag. This means that metrics which inherit from ExternalCounter are not required to have any tags. ExternalCounter excludes the "host" tag by default for the same reason.

#### Custom Metrics

>  Custom metric classes are classes which inherit _directly_ from `BosunMetric`. If you only inherit from the built-in metric types (e.g. `Counter`, `AggregateGauge`, etc.) then you don't need to do anything.

There are some breaking changes in how custom metric classes are implemented.

##### Serialize

The signature of `BosunMetric.Serialize` has changed from `IEnumerable<string> (string unixTimestamp)` to `void (MetricWriter writer, DateTime now)`. Instead of returning an enumeration of strings representing the serialized metrics, you call a protected method `WriteValue()` for each metric you want to serialize.

For example:

```csharp
protected override void Serialize(MetricWriter writer, DateTime now)
{
    WriteValue(writer, Value, now);
}
```

`WriteValue` takes an optional fourth parameter which is the suffix index (defaults to zero). See "Suffixes" below for more information on the meaning of the index.

`WriteValue` must only be called from inside the `Serialize` method, and should should not store a reference to the `MetricWriter`.

##### PreSerialize

The `Serialize` method should not be used to perform computationally expensive work because it is called in serial with all other metrics. Instead, expensive work should be performed in `PreSerialize`, which is called in parallel with other metrics that implement it.

For example, `AggregateGauge` uses `PreSerialize` to perform sorting and aggregation operations, and then it stores a snapshot which the `Serialize` method reads from.

```csharp
protected override void PreSerialize()
{
    // computationally expensive stuff here
}
```

##### Suffixes

BosunReporter has always supported multiple suffixes per metric. This is what allows `AggregateGauge` to serialize into several metrics. However, the implementation has changed slightly.

`IEnumerable<string> GetSuffixes()` has been replaced with `string[] GetImmutableSuffixesArray()`. As the name implies, the suffixes must be immutable for the lifetime of the metric.

Also, instead of passing around the string representation of the suffix, indexes into the suffix array are used (such as in the `WriteValue()` and `GetDescription()` methods).

##### Descriptions

As described above, the `GetDescription()` method now takes `int suffixIndex` instead of `string suffix`. This is an index into the array returned by `GetImmutableSuffixesArray()`.

