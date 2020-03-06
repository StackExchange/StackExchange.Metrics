using System;
using System.Collections.Generic;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Tests
{
    public static class TestHelper
    {
        public static IEnumerable<MetaData> GenerateMetadata(int count)
        {
            var metadata = new MetaData[count];
            for (var i = 0; i < metadata.Length; i++)
            {
                metadata[i] = new MetaData("test.metric_" + i, "desc", new Dictionary<string, string>(), "This is metadata!");
            }
            return metadata;
        }

        public static IEnumerable<MetricReading> GenerateReadings(int count)
        {
            var utcNow = DateTime.UtcNow;
            var readings = new MetricReading[count];
            for (var i = 0; i < readings.Length; i++)
            {
                readings[i] = new MetricReading("test.metric_" + i, MetricType.Counter, string.Empty, i, new Dictionary<string, string>(), utcNow.AddSeconds(i));
            }

            return readings;
        }
    }
}
