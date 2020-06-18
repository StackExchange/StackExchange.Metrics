# Tags

Tags are used to sub-divide data. For example, you shouldn't create two different metrics for `requests.successes` and `request.errors`. Instead, create one metric named `requests`, and use tags `result=success` and `result=error` to differentiate the data. On the other hand, if it wouldn't make sense to take the sum or average of two different pieces of data, then use separate metrics. For example, if you're tracking memory usage, you may want to know virtual memory size as well as private memory size. However, it wouldn't make sense to sum or average the two together (or any other aggregator), so they should be two different metric names.

Every metric should map to a single set of tag names. That is to say, you shouldn't use metric "my_metric" with the tags "host" and "route" in some cases, and the tags "machine" and "status" in other cases. Conceptually, the metric name could be thought of as similar to a variable name, and the list of tag names as its type. You can assign different instances of a type (different tag values) to a variable, but you can't assign an instance of a different type (different tag names) to that variable.

The library provides a liberal sprinkling of strong-typing over what is, in most metric platforms, a string => string mapping. For example to create a counter that has `route` and `result` tags:

```csharp
public enum Result
{
    Success,
    Error,
}

public class AppMetricSource : MetricSource
{
    public Counter<string, Result> Requests { get; }
    public AppMetricSource(MetricSourceOptions options) : base(options)
    {
        Requests = AddCounter("hits", "http requests", "Number of HTTP requests to the application", new MetricTag<string>("route"), new MetricTag<Result>("result"));
    }
}

// usage
source.Requests.Increment("Test/Route", Result.Success);

// or
source.Requests.Increment("Test/Route", Result.Error);
```

## Default tags

Default tags can be provided using the `MetricSourceOptions` passed into your `MetricSource`. For example, by using the dependency injection extensions:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddMetricsCollector()
        .ConfigureSources(
            options =>
            {
                // configure the "tier" tag
                options.DefaultTags["tier"] = "Local";
            }
        )
        .AddDefaultSources()
        .AddSource<AppMetricSource>();
}
```