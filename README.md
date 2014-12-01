# BosunReporter.NET

A thread-safe C# .NET client for reporting metrics to [Bosun (Time Series Alerting Framework)](http://bosun.org). This library is more than a simple wrapper around the JSON API. It is designed to encourage best-practices while making it easy to create counters and gauges, including pre-aggregated gauges. It automatically reports metrics on an interval and handles temporary API or network outages using a re-try queue.

> This is still in-development, is not on nuget yet, and has had very little testing. Use at your own risk.

## Usage

### Creating a BosunReporter

First, create a `BosunReporter` object. This is the top-level container which will hold all of your metrics and handle sending them to the Bosun API. Therefore, you'll probably only want to instantiate one, and make it a global singleton. 
 
 ```csharp
var reporter = new BosunReporter.BosunReporter(new BosunReporterOptions()
{
	MetricsNamePrefix = "app_name.",
	BosunUrl = "http://bosun.mydomain.com",
	PropertyToTagName = NameTransformers.CamelToLowerSnakeCase,
	DefaultTags = new Dictionary<string, string> { {"host", NameTransformers.Sanitize(Environment.MachineName.ToLower())} }
});
 ```

All of the available options are documented in the [BosunReporterOptions class](https://github.com/bretcope/BosunReporter.NET/blob/master/BosunReporter/BosunReporterOptions.cs).

### Creating Metrics

There are two types of metrics: gauges and counters. We'll create a counter called `my_counter` with only the default tags.

```csharp
var counter = reporter.GetMetric<BosunCounter>("my_counter");
```

Then increment it...

```csharp
counter.Increment();

// we could also increment it by more than 1
counter.Increment(23);
```
