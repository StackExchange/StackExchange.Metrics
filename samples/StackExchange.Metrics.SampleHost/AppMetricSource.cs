using System;
using StackExchange.Metrics.Metrics;

namespace StackExchange.Metrics.SampleHost
{
    public class AppMetricSource : MetricSource
    {
        private static readonly RandomNumberType[] _randomNumberTypes = (RandomNumberType[])Enum.GetValues(typeof(RandomNumberType));

        private readonly Counter<string> _requests;
        private readonly Counter<string, int> _errors;
        private readonly SamplingGauge _randomNumber;
        private readonly SamplingGauge<RandomNumberType> _randomNumberByType;

        private readonly Random _rng;

        public AppMetricSource(MetricSourceOptions options) : base(options)
        {
            _rng = new Random();
            _requests = AddCounter("request.hits", "requests", "Number of requests to the application, split by route", new MetricTag<string>("route"));
            _errors = AddCounter("request.errors", "requests", "Number of errors in the application, split by route and status code", new MetricTag<string>("route"), new MetricTag<int>("status_code"));
            _randomNumber = AddSamplingGauge("random", "number", "A random number");
            _randomNumberByType = AddSamplingGauge("random.by_type", "number", "A random number, split by an enum", new MetricTag<RandomNumberType>("type"));
        }

        public void OnRequest(string route)
        {
            _requests.Increment(route);
            _randomNumber.Record(_rng.NextDouble());
            _randomNumberByType.Record(_randomNumberTypes[_rng.Next(0, _randomNumberTypes.Length)], _rng.NextDouble());
        }

        public void OnError(string route, int statusCode) => _errors.Increment(route, statusCode);
    }

    public enum RandomNumberType
    {
        KindaRandom,
        ReallyRandom,
        NotRandom,
    }
}
