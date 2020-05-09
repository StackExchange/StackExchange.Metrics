using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Tests
{
    public static class TestHelper
    {
        public static IEnumerable<Metadata> GenerateMetadata(int count)
        {
            var metadata = new Metadata[count];
            for (var i = 0; i < metadata.Length; i++)
            {
                metadata[i] = new Metadata("test.metric_" + i, "desc", ImmutableDictionary<string, string>.Empty, "This is metadata!");
            }
            return metadata;
        }

        public static IEnumerable<MetricReading> GenerateReadings(int count)
        {
            var utcNow = DateTime.UtcNow;
            var readings = new MetricReading[count];
            for (var i = 0; i < readings.Length; i++)
            {
                readings[i] = new MetricReading("test.metric_" + i, MetricType.Counter, i, ImmutableDictionary<string, string>.Empty, utcNow.AddSeconds(i));
            }

            return readings;
        }
    }
}
