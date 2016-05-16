# BosunReporter.NET

[![NuGet version](https://badge.fury.io/nu/BosunReporter.svg)](http://badge.fury.io/nu/BosunReporter)
[![Build status](https://ci.appveyor.com/api/projects/status/yt8nl66ha598jbr7/branch/master?svg=true)](https://ci.appveyor.com/project/bretcope/bosunreporter-net/branch/master)

A thread-safe C# .NET client for reporting metrics to [Bosun (Time Series Alerting Framework)](http://bosun.org). This library is more than a simple wrapper around the JSON API. It is designed to encourage best-practices while making it easy to create counters and gauges, including multi-aggregate gauges. It automatically reports metrics on an interval and handles temporary API or network outages using a re-try queue.

__[VIEW CHANGES IN 3.0](](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#snapshotcounter))__

### Basic Usage

First, create a `MetricsCollector` object. This is the top-level container which will hold all of your metrics and handle sending them to the Bosun API. Therefore, you should only instantiate one, and make it a global singleton.

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

> All of the available options are documented in the [BosunOptions class](https://github.com/bretcope/BosunReporter.NET/blob/master/BosunReporter/BosunOptions.cs).

Create a counter with only the default tags:

```csharp
var counter = collector.CreateMetric<Counter>("my_counter", "units", "description");
```

Increment the counter by 1:

```csharp
counter.Increment();
```

### Using Tags

Tags are used to subdivide data in Bosun/OpenTSDB. In BosunReporter, tag sets are defined as C# classes. For example:

```csharp
public class SomeCounter : Counter
{
	[BosunTag] public readonly string SomeTag;
	
	public RouteCounter(string tag)
	{
		SomeTag = tag;
	}
}
```

For more details, see the [Tags Documentation](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/Tags.md).

### Metric Types

There are two high-level metric types: counters and gauges.

__[Counters](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#counters)__ are for _counting_ things. The most common use case is to increment a counter each time an event occurs. Bosun/OpenTSDB normalizes this data and is able to show you a rate (events per second) in the graphing interface. BosunReporter has two built-in counter types.

| Name                                     | Description                              |
| ---------------------------------------- | ---------------------------------------- |
| [Counter](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#counter) | A general-purpose manually incremented long-integer counter. |
| [SnapshotCounter](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#snapshotcounter) | Calls a user-provided `Func<long?>` to get the current counter value each time metrics are going to be posted to the Bosun API. |
| [ExternalCounter](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#externalcounter) | A persistent counter (no resets) for very low-volume events. |

__[Gauges](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#gauges)__ describe a measurement at a point in time. A good example would be measuring how much RAM is being consumed by a process. BosunReporter.NET provides several different built-in types of gauges in order to support different programmatic use cases, but Bosun itself does not differentiate between these types.

| Name                                     | Description                              |
| ---------------------------------------- | ---------------------------------------- |
| [SnapshotGauge](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#snapshotgauge) | Similar to a SnapshotCounter, it calls a user provided `Func<double?>` to get the current gauge value each time metrics are going to be posted to the Bosun API. |
| [EventGauge](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#eventgauge) | Every data point is sent to Bosun. Good for low-volume events. |
| [AggregateGauge](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#aggregategauge) | Aggregates data points (min, max, avg, median, etc) before sending them to Bosun. Good for recording high-volume events. |
| [SamplingGauge](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#samplinggauge) | Record as often as you want, but only the last value recorded before the reporting interval is sent to Bosun (it _samples_ the current value). |

If none of the built-in metric types meet your specific needs, it's easy to [create your own](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricTypes.md#create-your-own).

### Metric Groups

Metric groups allow you to easily setup metrics which share the same name, but with different tag values. [See Documentation](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricGroup.md).
