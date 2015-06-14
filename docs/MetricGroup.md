# MetricGroup

Because one of the goals of BosunReporter.NET is to encourage using tags to differentiate subsets of the same data (instead of separate metric names), metric groups are built into the library. A metric group is a collection of metrics with the same name, but with different tag values. For example, let's say we have a counter which counts every request. We might want to tag this counter to indicate how many requests were successful versus how many encountered an error.

Let's start by defining an enum of the result types we want to track.

```csharp
public enum Result
{
	Error,
	Success
}
```

Then we'll create a class for our counter.

```csharp
public class RequestCounter : Counter
{
	[BosunTag]
	public readonly Result Result;
	public RequestCounter(Result result) { Result = result; }
}
```

Now, instead of manually creating a metric for each of the result values we want (Success and Error), we can simply create a group which will enable us to create those metrics implicitly.

```csharp
var requestCounter = collector.GetMetricGroup<Result, RequestCounter>(
                                "requests",                            // name
                                "requests",                            // unit
                                "A count of the number of requests",   // description
                                result => new RequestCounter(result)); // factory

// MetricGroup.Add() creates a metric if it does not already exist, and returns that metric.
// Best-practice is to call Add() as close to application-startup as possible to avoid 
// "Unknown" Bosun alerts.
requestCounter.Add(Result.Success);
requestCounter.Add(Result.Error);

// once they've been added, we can use indexer syntax to access those metrics. For example...
requestCounter[Result.Success].Increment();
```

Note that we used the generic type arguments `<Result, RequestCounter>` in the example above. This determines the signature of the factory, which is `Func<Result, RequestCounter>` in this example because it takes a single `Result` argument, and returns a `RequestCounter`. It also determines the method signature of `Add()`.

> Because `RequestCounter` has a constructor which matches the type signature provided (accepts a single Result, and returns an instance of RequestCounter), we could have omitted the factory argument. The metric group will use that constructor to generate a default factory.

## Multiple Tags

You can also create metric groups which is differentiated by more than one tag (MetricGroup currently supports up to 5, though splitting on more than 2 or 3 may be a code-smell). Simply add additional type arguments to the MetricGroup generic type constructor. Let's use a three-tag counter as an example:

```csharp
public ThreeTagCounter : Counter
{
	[BosunTag] public readonly string One;
	[BosunTag] public readonly string Two;
	[BosunTag] public readonly SomeEnum Three;
	
	public ThreeTagCounter(string one, int two, SomeEnum three)
	{
		One = one;
		Two = two.ToString();
		Three = three;
	}
}
```

Now create a metric group for the counter:

```csharp
var group = collector.GetMetricGroup<string, int, SomeEnum, ThreeTagCounter>(
                                                                    "my_group",
                                                                    "units",
                                                                    "description")

group.Add("hello", 2, SomeEnum.MyValue).Increment();

// indexer syntax also works, as long as Add() has been previously called
// with the same argument values
group["hello", 2, SomeEnum.MyValue].Increment();
```

But suppose we wanted a group for that same counter where the `One` tag is _always_ "hello", and we only split on the other two tags. We could do this by defining our own factory method.

```csharp
var helloGroup = collector.GetMetricGroup<int, SomeEnum, ThreeTagCounter>(
                                 "my_group",
                                 "units",
                                 "description",
                                 (two, three) => new ThreeTagCounter("hello", two, three));

helloGroup.Add(2, SomeEnum.MyValue).Increment();
helloGroup.Add(7, SomeEnum.AnotherValue).Increment();
```

> Value types or strings are always best for the metric group arguments. Objects are okay as long as they play well as dictionary keys or values inside a Tuple which is used as a dictionary key. This generally means any object used as a metric group argument should implement IEquatable<T> and have a good GetHashCode implementation.

## Auto-Populate from Enum Values

In the first example, we populated all of the values of our `Result` enum into the group using:

```csharp
requestCounter.Add(Result.Success);
requestCounter.Add(Result.Error);
```

Since it's very common to want one metric per enum value, there is a helper method which does just that. The code below is functionally equivalent to the code above.

```csharp
requestCounter.PopulateFromEnum();
```

However, this helper method is only available when there is only one input type parameter, and that type parameter is an enum type. For example, you could use `PopulateFromEnum` on a group with the type signature `MetricGroup<SomeEnum, SomeMetric>`, but not on `MetricGroup<SomeEnum, int, SomeMetric>` because it has more than one input type parameter (`SomeEnum` and `int`).
