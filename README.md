# BosunReporter.NET

A thread-safe C# .NET client for reporting metrics to [Bosun (Time Series Alerting Framework)](http://bosun.org). This library is more than a simple wrapper around the JSON API. It is designed to encourage best-practices while making it easy to create counters and gauges, including multi-aggregate gauges. It automatically reports metrics on an interval and handles temporary API or network outages using a re-try queue.

## Usage

* [MetricsCollector](#metricscollector)
* [Metric Types](#metric-types)
* [Counters](#counters)
* [Tags](#tags)
* [Snapshot Gauges](#snapshot-gauges)
* [Event Gauges](#event-gauges)
* [Aggregate Gauges](#aggregate-gauges)
* [Sampling Gauges](#sampling-gauges)

### MetricsCollector

First, create a `MetricsCollector` object. This is the top-level container which will hold all of your metrics and handle sending them to the Bosun API. Therefore, you'll probably only want to instantiate one, and make it a global singleton.
 
 ```csharp
var collector = new MetricsCollector(new BosunOptions()
{
	MetricsNamePrefix = "app_name.",
	BosunUrl = "http://bosun.mydomain.com:8070",
	PropertyToTagName = NameTransformers.CamelToLowerSnakeCase,
	DefaultTags = new Dictionary<string, string> 
		{ {"host", NameTransformers.Sanitize(Environment.MachineName.ToLower())} }
});
 ```

All of the available options are documented in the [BosunOptions class](https://github.com/bretcope/BosunReporter.NET/blob/master/BosunReporter/BosunOptions.cs).

### Metric Types

Bosun supports two high-level metric types.

1. __[Counters](#counters)__ These are for _counting_ things. The most common use case is to increment a counter each time an event occurs. Bosun/OpenTSDB normalizes this data and is able to show you a rate (events per second) in the graphing interface.
2. __Gauges__ describe a measurement at a point in time. A good example would be measuring how much RAM is being consumed by a process. BosunReporter.NET provides several different types of gauges in order to support different programmatic use cases, but Bosun itself does not differentiate between these types. 
    * [Snapshot](#snapshot-gauges) - You provide a callback which will be called at every reporting interval. The value that the callback returns is reported.
    * [Event](#event-gauges) - Every data point is sent to Bosun. Good for low-volume events.
    * [Aggregate](#aggregate-gauges) - Aggregates data points (min, max, avg, median, etc) before sending them to Bosun. Good for recording high-volume events.
    * [Sampling](#sampling-gauges) - Record as often as you want, but only the last value recorded before the reporting interval is sent to Bosun (it _samples_ the current value).

### Counters

Let's start by creating a counter called `my_counter` with only the default tags.

```csharp
var counter = collector.GetMetric<BosunCounter>("my_counter");
```

Then increment it by one...

```csharp
counter.Increment();
```

or increment it by more than 1

```csharp
counter.Increment(23);
```

This metric, like all other metrics, will be automatically sent to Bosun every 30 seconds, or on the interval defined by [BosunOptions.ReportingInterval](https://github.com/bretcope/BosunReporter.NET/blob/master/BosunReporter/BosunOptions.cs#L45).

### Tags

Every metric should map to a single set of tag names. That is to say, you shouldn't use metric "my_metric" with the tags "host" and "route" in some cases, and the tags "machine" and "status" in other cases. Conceptually, the metric name could be thought of as similar to a variable name, and the list of tag names as its type. You can assign different instances of a type (different tag values) to a variable, but you can't assign an instance of a different type (different tag names).

It turns out, a good way to enforce this behavior is simply to create classes for the list of tags you need. Let's say we want a route hit counter which, in addition to the default tags, has `route` and `result` tags.

```csharp
public class RouteCounter : BosunCounter
{
	[BosunTag]
	public readonly string Route;
	[BosunTag]
	public readonly string Result;
	
	public RouteCounter(string route, bool ok)
	{
		Route = route;
		Result = ok ? "ok" : "error";
	}
}
```

And then let's instantiate one.

```csharp
var testRouteOkCounter = collector.GetMetric("hits", new RouteCounter("Test/Route", true));
```

The metric name `hits` has now been bound to the `RouteCounter` type. If we try to use that metric name with any other type, the library will throw an exception. However, we can use that metric with as many different instances of `RouteCounter` as we'd like. For example:

```csharp
var testRouteErrorCounter = collector.GetMetric("hits", new RouteCounter("Test/Route", false));
```

It it worth noting that the `GetMetric()` method is idempotent, so you'll never end up with duplicate metrics.

```csharp
var one = collector.GetMetric("hits", new RouteCounter("Test/Route", true));
var two = collector.GetMetric("hits", new RouteCounter("Test/Route", true));

// one == two - they are the same object
```

This, like the rest of the library, is thread safe. You could use this method to always instantiate and use metrics on-demand. Although, if you're concerned with performance, it is computationally cheaper to store the metrics in variables or a hash rather than calling `GetMetric()` every time you need it.

This `RouteCounter` type we just created, and any other BosunMetric type, can be used with as many metric names as you'd like. __You don't have to create a class for every metric you use__ if they share common tag lists. In fact, using common tag lists is a great idea which will help encourage consistency in your metrics conventions.

### Snapshot Gauges

These are great for metrics where you want to record snapshots of a value, like CPU or memory usage. Pretend we have a method called `GetMemoryUsage` which returns a double. Now, let's write a snapshot gauge which calls that automatically at every metrics reporting/snapshot interval.

```csharp
collector.GetMetric("memory_usage", new BosunSnapshotGauge(() => GetMemoryUsage()));
```

That's it. There's no reason to even assign the gauge to a variable.

> __Why the lambda instead of just passing `GetMemoryUsage` to the constructor?__ In this contrived example, I said `GetMemoryUsage` returns a double. The BosunSnapshotGauge constructor actually accepts a `Func<double?>`. It calls this function right before metrics are about to be flushed. If it returns a double value, the gauge reports that value. If it returns null, then the gauge does not report anything. This way, you have the flexibility of only reporting on something when there is sensible data to report.

### Event Gauges

These are ideal for low-volume event-based data where it's practical to send all of the data points to Bosun. If you have a measurable event which occurs once every few seconds, then, instead of aggregating, you may want to use an event gauge. Every time you call `.Record()` on an event gauge, the metric will be serialized and queued. The queued metrics will be sent to Bosun on the normal reporting interval, like all other metrics.

```csharp
var myEvent = collector.GetMetric("my_event", new BosunEventGauge());
someObject.OnSomeEvent += (sender, e) => myEvent.Record(someObject.Value);
```

### Aggregate Gauges

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
public class RouteTimingGauge : BosunAggregateGauge
{
	[BosunTag]
	public readonly string Route;

	public TestAggregateGauge(string route)
	{
		Route = route
	}
}
```

Then, instantiate the gauge for our route, and record timings to it.
 
```csharp
var testRouteTiming = collector.GetMetric("route_tr", new RouteTimingGauge("Test/Route"));
testRouteTiming.Record(requestDuration);
```

If median or percentile aggregators are used, then all values passed to the `Record()` method are stored until the next reporting interval, and must be sorted at that time in order to calculate the aggregate values. If you're concerned about this performance overhead, run some benchmarks on sorting a `List<double>` where the count is the number of data points you expect in-between metric reporting intervals. When there are multiple gauge metrics, the sorting is performed in parallel.

### Sampling Gauges

A sampling gauge simply reports the last recorded value at every reporting interval. They are similar to an aggregate gauge which only uses the "Last" aggregator. However, there are two differences:
 
1. In a sampling gauge, if no data has been recorded in the current snapshot/reporting interval, then the value from the previous interval is used. Whereas, an aggregate gauge won't report anything if no data was recorded during the interval.
2. The sampling gauge does not use locks to achieve thread safety, so it should perform slightly better than the "Last" aggregator, especially in highly concurrent environments.

If the last recorded value is `Double.NaN`, then nothing will be reported to Bosun.

```csharp
var sampler = collector.GetMetric("my_sampler", new BosunSamplingGauge());
sampler.Record(1.2);
```
