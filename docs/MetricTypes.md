# Metric Types

## Counters

Counters are for _counting_ things. The most common use case is to increment a counter each time an event occurs. Bosun/OpenTSDB normalizes this data and is able to show you a rate (events per second) in the graphing interface.

### Counter

This is the basic counter type. It uses a `long` value and calls `Interlocked.Add()` internally to incrementing the value.

```csharp
var counter = collector.GetMetric<Counter>("my_counter", "units", "description");

// increment by 1
counter.Increment();

// increment by more than 1
counter.Increment(23);
```

### SnapshotCounter

A snapshot counter is useful when you only care about updating the counter once per-reporting interval. The constructor takes a callback with the signature `Func<long?>` which will be called once per reporting interval. If the callback returns `null`, then no value is reported for the current interval.

```csharp
var count = 0;
collector.GetMetric("name", "unit", "desc", new SnapshotCounter(() => count++));
```

## Gauges

Gauges describe a measurement at a point in time. A good example would be measuring how much RAM is being consumed by a process. BosunReporter.NET provides several different types of gauges in order to support different programmatic use cases, but Bosun itself does not differentiate between these types. 

### SnapshotGauge

These are great for metrics where you want to record snapshots of a value, like CPU or memory usage. Pretend we have a method called `GetMemoryUsage` which returns a double. Now, let's write a snapshot gauge which calls that automatically at every metrics reporting interval.

```csharp
collector.GetMetric("memory_usage", units, desc, new SnapshotGauge(() => GetMemoryUsage()));
```

That's it. There's no reason to even assign the gauge to a variable.

> __Why the lambda instead of just passing `GetMemoryUsage` to the constructor?__ In this contrived example, I said `GetMemoryUsage` returns a double. The SnapshotGauge constructor actually accepts a `Func<double?>`. It calls this function right before metrics are about to be flushed. If it returns a double value, the gauge reports that value. If it returns null, then the gauge does not report anything. This way, you have the flexibility of only reporting on something when there is sensible data to report.

### EventGauge

These are ideal for low-volume event-based data where it's practical to send all of the data points to Bosun. If you have a measurable event which occurs once every few seconds, then, instead of aggregating, you may want to use an event gauge. Every time you call `.Record()` on an event gauge, the metric will be serialized and queued. The queued metrics will be sent to Bosun on the normal reporting interval, like all other metrics.

```csharp
var myEvent = collector.GetMetric<EventGauge>("my_event", units, desc);
someObject.OnSomeEvent += (sender, e) => myEvent.Record(someObject.Value);
```

### AggregateGauge

These are useful for event-based gauges where the volume of data points makes it undesirable or impractical to send them all to Bosun. For example, imagine you want to capture performance timings from five individual parts of your web request pipeline, and then report those numbers to Bosun. You might not want the number of metrics you send to Bosun to be 5x the number of your web requests, so the solution is to send aggregates.

Aggregate gauges come with six aggregators to choose from. You must use at least one for each gauge, but you can use as many as you'd like. BosunReporter.NET automatically expands the gauge into multiple metrics when sending to Bosun by appending suffixes to the metric name based on the aggregators in use.

Name       | Default Suffix | Description
-----------|----------------|------------
Average    | `_avg`         | The arithmetic mean.
Median     | `_median`      | 50th percentile.
Percentile | `_%%`          | Allows you to specify an arbitrary percentile (i.e. `0.95` for the 95th percentile). The default suffix is the integer representation of the percentage (i.e. `_95`).
Max        | `_max`         | The highest recorded value.
Min        | `_min`         | The lowest recorded value.
Last       | (no suffix)    | The last recorded value before the reporting/snapshot interval.

All aggregators are reset at each reporting/snapshot interval. If no data points have been recorded since the last reporting interval, then nothing will be sent to Bosun since there is effectively no data.

Let's create a simple route-timing metric which has a `route` tag, and reports on the median, 95th percentile, and max values. First, create a class which defines this gauge type.

```csharp
[GaugeAggregator(AggregateMode.Max)]
[GaugeAggregator(AggregateMode.Median)]
[GaugeAggregator(AggregateMode.Percentile, 0.95)]
public class RouteTimingGauge : AggregateGauge
{
	[BosunTag] public readonly string Route;

	public TestAggregateGauge(string route)
	{
		Route = route
	}
}
```

Then, instantiate the gauge for our route, and record timings to it.
 
```csharp
var testRouteTiming = collector.GetMetric(
                                          "route_tr",
                                          units,
                                          desc,
                                          new RouteTimingGauge("Test/Route"));

testRouteTiming.Record(requestDuration);
```

If median or percentile aggregators are used, then _all_ values passed to the `Record()` method are stored in a `List<double>` until the next reporting interval, and must be sorted at that time in order to calculate the aggregate values. If you're concerned about this performance overhead, run some benchmarks on sorting a `List<double>` where the count is the number of data points you expect in-between metric reporting intervals. When there are multiple gauge metrics, the sorting is performed in parallel.

> Aggregate gauges use locks to achieve thread-safety currently. This is an implementation detail which may change if there is concurrency pattern which is shown to improve performance in highly parallel environments. Using a spin-wait pattern was also tried, but didn't yield any detectable improvement in testing.

### SamplingGauge

A sampling gauge simply reports the last recorded value at every reporting interval. They are similar to an aggregate gauge which only uses the "Last" aggregator. However, there are two differences:
 
1. In a sampling gauge, if no data has been recorded in the current snapshot/reporting interval, then the value from the previous interval is used. Whereas, an aggregate gauge won't report anything if no data was recorded during the interval.
2. The sampling gauge does not use locks to achieve thread safety, so it should perform slightly better than the "Last" aggregator, especially in highly concurrent environments.

If the last recorded value is `Double.NaN`, then nothing will be reported to Bosun.

```csharp
var sampler = collector.GetMetric<SamplingGauge>("my_sampler", units, desc);
sampler.Record(1.2);
```

## Create Your Own

The built-in metric types described above should work for most use cases. However, you can also write your own by inheriting from the abstract `BosunMetric` class and writing an implementation for the `MetricType` property, and the `GetSerializedMetrics` method.

Both of the built-in counter types use a `long` as their value type. Here's how we might implement a floating point counter:

```csharp
public class DoubleCounter : BosunMetric, IDoubleCounter
{
	// determines whether the metric is treated as a counter or gauge
	public override string MetricType { get { return "counter"; } }
	
	private object _lock = new object();
	public double Value { get; private set; }
	
	public void Increment(double amount = 1.0)
	{
		// Interlocked doesn't have an Increment() for doubles, so we have to use another
		// concurrency strategy. You should always keep thread-safety in mind when designing
		// your own metrics.
		lock (_lock) 
		{
			Value += amount;
		}
	}
	
	// this method is called by the collector when it's time to post to the Bosun API
	protected override IEnumerable<string> GetSerializedMetrics(string unixTimestamp)
	{
		// ToJson is a protected method on BosunMetric
		yield return ToJson("", Value, unixTimestamp);
	}
}
```

> Implementing the [IDoubleCounter](https://github.com/bretcope/BosunReporter.NET/blob/master/BosunReporter/Infrastructure/MetricInterfaces.cs#L18) interface is optional, but good practice.

Notice how `GetSerializedMetrics` returns an `IEnumerable<string>` instead of a single string. This is what enables the [AggregateGauge](#aggregategauge) to serialize into multiple metrics with different suffixes. The first argument to `ToJson` is a suffix. In this example, we only have one suffix, which is an empty string.

__If you choose to use any other suffix__, or multiple suffixes, you must also override the `protected virtual IEnumerable<string> GetSuffixes()` method so that it returns the suffixes in use. This method is called only once (the first time the `BosunMetric.Suffixes` property is accessed). The results are cached and remain immutable for the lifetime of the metric. BosunReporter uses this list to ensure there are no name collisions. If you attempt to call `ToJson` with a suffix not in the list, an exception will be thrown.

For more examples, simply look at how the built-in metric types are implemented. See [/BosunReporter/Metrics](https://github.com/bretcope/BosunReporter.NET/tree/master/BosunReporter/Metrics)
