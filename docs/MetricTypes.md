# Metric Types

## Counters

Counters are for _counting_ things. The most common use case is to increment a counter each time an event occurs. Many metric platforms normalizes this data and is able to show you a rate (events per second) in the graphing interface.

### Counter

This is the basic counter type. It uses a `long` value and calls `Interlocked.Add()` internally to incrementing the value.

```csharp
var counter = source.AddCounter("my_counter", "units", "description");

// increment by 1
counter.Increment();

// increment by more than 1
counter.Increment(23);
```

### SnapshotCounter

A snapshot counter is useful when you only care about updating the counter once per-reporting interval. The constructor takes a callback with the signature `Func<long?>` which will be called once per reporting interval. If the callback returns `null`, then no value is reported for the current interval.

```csharp
var count = 0;
var counter = source.AddSnapshotCounter(() => count++, "name", "unit", "desc");
```

### CumulativeCounter

>  In Bosun, this feature requires you to be using [tsdbrelay](https://github.com/bosun-monitor/bosun/tree/master/cmd/tsdbrelay) as an intermediary between your app and Bosun. You'll need to run tsdbrelay with `-redis=REDIS_SERVER_NAME` and setup an [scollector](https://github.com/bosun-monitor/bosun/tree/master/cmd/scollector) instance to scrape it with:
>
> ```
> [[RedisCounters]] 
> Server = "localhost:6379" 
> Database = 2
> ```

Cumulative counters are intended to solve the problem of counting low-volume events.

The nature of a low-volume counter is that its per-second rate is going to be zero most of the time. For example:

![](https://i.stack.imgur.com/qD8Ki.png)

If you could simply see the start and end values for a given time interval, you would have a better sense of how frequent the events are. But, unfortunately, a normal counter resets every time the application restarts, so you end up with a graph that might look something like this when viewed as a gauge:

![](https://i.stack.imgur.com/wwGrO.png)

To solve this problem, cumulative counters are persistent (the value doesn't reset every time the app restarts). A metrics platform stores the value of the counter, and StackExchange.Metrics sends it increments when an event happens.

This means that when you graph the metric as a gauge, it will always be going up, and you can easily see start and end values for any time interval.

Remember, __these are for LOW VOLUME__ events. If you expect more than one event per minute across all instances of your app, you should use a normal counter.

For Bosun, you can enable/disable sending cumulative counter increments using `BosunMetricHandler.EnableExternalCounters` during initialization, or by changing `BosunMetricHandler.EnableExternalCounters` at runtime.

#### Usage

The usage of `CumulativeCounter` is exactly the same as `Counter` except that you can only increment by 1.

```csharp
var counter = source.AddCumulativeCounter("ext_counter", "units", "description");
counter.Increment();
```

## Gauges

Gauges describe a measurement at a point in time. A good example would be measuring how much RAM is being consumed by a process. StackExchange.Metrics provides several different types of gauges in order to support different programmatic use cases.

### SnapshotGauge

These are great for metrics where you want to record snapshots of a value, like CPU or memory usage. Pretend we have a method called `GetMemoryUsage` which returns a double. Now, let's write a snapshot gauge which calls that automatically at every metrics reporting interval.

```csharp
source.AddSnapshotGauge("memory_usage", units, desc, new SnapshotGauge(() => GetMemoryUsage()));
```

That's it. There's no reason to even assign the gauge to a variable.

> __Why the lambda instead of just passing `GetMemoryUsage` to the constructor?__ In this contrived example, I said `GetMemoryUsage` returns a double. The SnapshotGauge constructor actually accepts a `Func<double?>`. It calls this function right before metrics are about to be flushed. If it returns a double value, the gauge reports that value. If it returns null, then the gauge does not report anything. This way, you have the flexibility of only reporting on something when there is sensible data to report.

### EventGauge

These are ideal for low-volume event-based data where it's practical to send all of the data points to a metrics platform. If you have a measurable event which occurs once every few seconds, then, instead of aggregating, you may want to use an event gauge. Every time you call `.Record()` on an event gauge, the metric will be serialized and queued. The queued metrics will be sent to metrics handlers on the normal reporting interval, like all other metrics.

```csharp
var myEvent = source.AddEventGauge("my_event", units, desc);
someObject.OnSomeEvent += (sender, e) => myEvent.Record(someObject.Value);
```

### AggregateGauge

These are useful for event-based gauges where the volume of data points makes it undesirable, or impractical, to send them all to a metrics platform. For example, imagine you want to capture performance timings from five individual parts of your web request pipeline, and then report those numbers to the metrics handlers. You might not want the number of metrics you send to the metrics handlers to be 5x the number of your web requests, so the solution is to send aggregates.

Aggregate gauges come with six aggregators to choose from. You must use at least one for each gauge, but you can use as many as you'd like. StackExchange.Metrics automatically expands the gauge into multiple metrics when sending to the metrics platform by appending suffixes to the metric name based on the aggregators in use.

| Name       | Default Suffix | Description                              |
| ---------- | -------------- | ---------------------------------------- |
| Average    | `_avg`         | The arithmetic mean.                     |
| Median     | `_median`      | 50th percentile.                         |
| Percentile | `_%%`          | Allows you to specify an arbitrary percentile (i.e. `0.95` for the 95th percentile). The default suffix is the integer representation of the percentage (i.e. `_95`). |
| Max        | `_max`         | The highest recorded value.              |
| Min        | `_min`         | The lowest recorded value.               |
| Last       | (no suffix)    | The last recorded value before the reporting/snapshot interval. |
| Count      | `_count`       | The number of events recorded during the reporting interval. |

All aggregators are reset at each reporting/snapshot interval. If no data points have been recorded since the last reporting interval, then only the `Count` aggregator (if present) will be sent to the metrics handlers.

> By default, the minimum number of events which must be recorded before the AggregateGauge will report anything is one event per reporting interval. You can change this default by assigning your own `Func<int>` to the static `AggregateGauge.GetDefaultMinimumEvents` property. Or, you can override the `AggregateGauge.MinimumEvents` property on classes which inherit from AggregateGauge. This squelch feature does not apply to `Count` aggregators, which always report, regardless of how many events were recorded.

Let's create a simple route-timing metric which has a `route` tag, and reports on the median, 95th percentile, and max values.

```csharp
var aggregators = new[] { GaugeAggregator.Max, GaugeAggregator.Median, GaugeAggregator.Percentile_95 };
var gauge = source.AddAggregateGauge(aggregators, "route_rt", units, desc, new MetricTag<string>("route));
gauge.Record("Test/Route", requestDuration);
```

If median or percentile aggregators are used, then _all_ values passed to the `Record()` method are stored in a `List<double>` until the next reporting interval, and must be sorted at that time in order to calculate the aggregate values. If you're concerned about this performance overhead, run some benchmarks on sorting a `List<double>` where the count is the number of data points you expect in-between metric reporting intervals.

> Aggregate gauges use locks to achieve thread-safety currently. This is an implementation detail which may change if there is concurrency pattern which is shown to improve performance in highly parallel environments. Using a spin-wait pattern was also tried, but didn't yield any detectable improvement in testing.

### SamplingGauge

A sampling gauge simply reports the last recorded value at every reporting interval. They are similar to an aggregate gauge which only uses the "Last" aggregator. However, there are two differences:

1. In a sampling gauge, if no data has been recorded in the current snapshot/reporting interval, then the value from the previous interval is used. Whereas, an aggregate gauge won't report anything if no data was recorded during the interval.
2. The sampling gauge does not use locks to achieve thread safety, so it should perform slightly better than the "Last" aggregator, especially in highly concurrent environments.

If the last recorded value is `Double.NaN`, then nothing will be reported to the metrics handlers.

```csharp
var sampler = collector.CreateMetric<SamplingGauge>("my_sampler", units, desc);
sampler.Record(1.2);
```

## Advanced Usage

### Create Your Own

The built-in metric types described above should work for most use cases. However, you can also write your own by inheriting from the abstract `MetricBase` class and writing an implementation for the `MetricType` property, and the `Write` method.

Both of the built-in counter types use a `long` as their value type. Here's how we might implement a floating point counter:

```csharp
public class DoubleCounter : MetricBase
{
	private readonly object _lock = new object();
	private double _value;

	public DoubleCounter(string name, string unit, string description, MetricSourceOptions options, ImmutableDictionary<string, string> tags = null) : base(name, unit, description, options, tags)
	{
	}

	// determines whether the metric is treated as a counter or gauge
	public override MetricType MetricType { get; } = MetricType.Counter;

	public void Increment(double amount = 1.0)
	{
		// Interlocked doesn't have an Increment() for doubles, so we have to use another
		// concurrency strategy. You should always keep thread-safety in mind when designing
		// your own metrics.
		lock (_lock)
		{
			_value += amount;
		}
	}

	// this method is called by the collector when it's time to post to the metrics handlers
	protected override void Write(IMetricReadingBatch batch, DateTime timestamp)
	{
		var countSnapshot = Interlocked.Exchange(ref _count, 0);
		if (countSnapshot == 0)
		{
			return;
		}

		batch.Add(
			CreateReading(countSnapshot, timestamp)
		);
	}
}
```

To make it easy to add your metric to a `MetricSource` you should create extension methods to help consumers.

```csharp
public static class MetricSourceExtensions
{
	public  static DoubleCounter AddDoubleCounter(this MetricSource source, string name, string unit, string description) => source.Add(new DoubleCounter(name, unit, description, source.Options));
}
```

### Tagging

If you want to support adding tags to your new metric you should provide an implementation of `TaggedMetricFactory<TMetric, TTag1, ..., TTagN>` and extension methods to support its use by consumers.

```csharp
public class DoubleCounter<TTag1> : TaggedMetricFactory<DoubleCounter, TTag1>
{
	internal Counter(string name, string unit, string description, in MetricTag<TTag1> tag1, MetricSourceOptions options) : base(name, unit, description, options, tag1) { }

	public void Increment(TTag1 tag1, double amount = 1.0d) => GetOrAdd(tag1).Increment(amount);

	protected override DoubleCounter Create(ImmutableDictionary<string, string> tags) => new DoubleCounter(Name, Unit, Description, Options, tags);
}

public class DoubleCounter<TTag1, TTag2> : TaggedMetricFactory<DoubleCounter, TTag1, TTag2>
{
	internal Counter(string name, string unit, string description, in MetricTag<TTag1> tag1, in MetricTag<TTag2> tag2, MetricSourceOptions options) : base(name, unit, description, options, tag1, tag2) { }

	public void Increment(TTag1 tag1, TTag1 tag2, double amount = 1.0d) => GetOrAdd(tag1, tag2).Increment(amount);

	protected override DoubleCounter Create(ImmutableDictionary<string, string> tags) => new DoubleCounter(Name, Unit, Description, Options, tags);
}

// and so on for as many tags as you want to support

public class DoubleCounter<TTag1, TTag2, ..., TTagN> : TaggedMetricFactory<DoubleCounter, TTag1, TTag2, ..., TTagN>
{
	internal Counter(string name, string unit, string description, in MetricTag<TTag1> tag1, in MetricTag<TTag2> tag2, ..., in MetricTag<TTagN> tagN, MetricSourceOptions options) : base(name, unit, description, options, tag1, tag2, ..., tagN) { }

	public void Increment(TTag1 tag1, TTag1 tag2, ..., TTagN tagN, double amount = 1.0d) => GetOrAdd(tag1, tag2, ..., tagN).Increment(amount);

	protected override DoubleCounter Create(ImmutableDictionary<string, string> tags) => new DoubleCounter(Name, Unit, Description, Options, tags);
}

public static class MetricSourceExtensions
{
	public static DoubleCounter<TTag> AddDoubleCounter<TTag>(this MetricSource source, string name, string unit, string description, in MetricTag<TTag> tag) 	=> source.Add(new DoubleCounter<TTag>(name, unit, description, tag, source.Options));

	public static DoubleCounter<TTag1, TTag2> AddDoubleCounter<TTag1, TTag2>(this MetricSource source, string name, string unit, string description, in MetricTag<TTag1> tag1, in MetricTag<TTag2> tag2) 
		=> source.Add(new DoubleCounter<TTag1, TTag2>(name, unit, description, tag1, tag2, source.Options));

	// and so on for as many tags as you want to support

	public static DoubleCounter<TTag1, TTag2, ..., TTagN> AddDoubleCounter<TTag1, TTag2>(this MetricSource source, string name, string unit, string description, in MetricTag<TTag1> tag1, in MetricTag<TTag2> tag2, ..., in MetricTag<TTagN> tagN) 
		=> source.Add(new DoubleCounter<TTag1, TTag2, ..., TTagN>(name, unit, description, tag1, tag2, ..., tagN, source.Options));
}
```

### Multiple Suffixes

Most metrics don't need multiple suffixes; however, it's supported in the case that a single instance of a metric class actually needs to serialize as multiple metrics. The primary use case is `AggregateGauge` which serializes into multiple aggregates (e.g. `metric_avg`, `metric_max`, etc.).

The default is to have a single empty string suffix, but if your custom metric type needs to support multiple suffixes, then you'll need to override `GetSuffixMetadata()`:

```csharp
protected virtual IEnumerable<SuffixMetadata> GetSuffixMetadata()
{
	yield return new SuffixMetadata(Name + "_avg", Unit, Description + " (avg)");
	yield return new SuffixMetadata(Name + "_max", Unit, Description + " (max)");
}
```

This list of suffixes must be immutable for the lifetime of the metric; it is captured once when the metric is initialized.

### Examples

For more examples, simply look at how the built-in metric types are implemented. See [/src/StackExchange.Metrics/Metrics](https://github.com/StackExchange/StackExchange.Metrics/tree/master/src/StackExchange.Metrics/Metrics)
