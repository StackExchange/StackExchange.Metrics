# Tags

Tags are used to sub-divide data. For example, you shouldn't create two different metrics for `requests.successes` and `request.errors`. Instead, create one metric named `requests`, and use tags `result=success` and `result=error` to differentiate the data. On the other hand, if it wouldn't make sense to take the sum or average of two different pieces of data, then use separate metrics. For example, if you're tracking memory usage, you may want to know virtual memory size as well as private memory size. However, it wouldn't make sense to sum or average the two together (or any other aggregator), so they should be two different metric names.

Every metric should map to a single set of tag names. That is to say, you shouldn't use metric "my_metric" with the tags "host" and "route" in some cases, and the tags "machine" and "status" in other cases. Conceptually, the metric name could be thought of as similar to a variable name, and the list of tag names as its type. You can assign different instances of a type (different tag values) to a variable, but you can't assign an instance of a different type (different tag names) to that variable.

It turns out, a good way to enforce this behavior is simply to create classes for the list of tags you need. Let's say we want a route hit counter which, in addition to the default tags, has `route` and `result` tags.

```csharp
public enum Result
{
    Success,
    Error,
}

public class RouteCounter : Counter
{
	[BosunTag] public readonly string Route;
	[BosunTag] public readonly Result Result;
	
	public RouteCounter(string route, Result result)
	{
		Route = route;
		Result = result;
	}
}
```

> Tags are expressed as readonly fields marked with the `BosunTag` attribute. Only string and enum types are allowed.

And then let's instantiate one.

```csharp
var testRouteOkCounter = collector.CreateMetric(
                                      "hits",          // metric name
                                      "http requests", // units
                                      "description",   // meaningful description
                                      new RouteCounter("Test/Route", Result.Success));
```

The metric name `hits` has now been bound to the `RouteCounter` type. If we try to use that metric name with any other type, the library will throw an exception. However, we can use that metric name with as many different instances of `RouteCounter` as we'd like. For example:

```csharp
var testRouteErrorCounter = collector.CreateMetric(
                                      "hits",
                                      "units",
                                      "description",
                                      new RouteCounter("Test/Route", Result.Error));
```

It is worth noting that `CreateMetric()` will throw an exception if you try to create a duplicate metric. That behavior is generally desirable since multiple attempts to create the same metric likely represent a mistake. However, there is also a `GetMetric()` method (with identical signatures) which is idempotent. It still won't allow you to create duplicate metrics, but instead of throwing an exception if an identical metric already exists, it will return the existing metric.

```csharp
var one = collector.GetMetric("hits", units, desc, new RouteCounter("Test/Route", true));
var two = collector.GetMetric("hits", units, desc, new RouteCounter("Test/Route", true));

Console.WriteLine(one == two); // outputs True
```

This, like the rest of the library, is thread safe. You _could_ use this method to always instantiate and use metrics on-demand. However, if you care about performance or allocations, it is cheaper to store the metrics in variables or a hash rather than calling `GetMetric()` every time you need it. Or even better, consider using a [Metric Group](https://github.com/bretcope/BosunReporter.NET/blob/master/docs/MetricGroup.md) to create your metrics.

> This `RouteCounter` type we just created, and any other BosunMetric type, can be used with as many metric names as you'd like. __You don't have to create a class for every metric you use__ if they share common tag lists. In fact, using common tag lists is a great idea which will help encourage consistency in your metrics conventions.
