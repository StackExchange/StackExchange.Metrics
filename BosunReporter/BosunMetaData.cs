using System;
using System.Collections.Generic;
using System.Linq;

namespace BosunReporter
{
    internal class BosunMetaData
    {
        public string Metric;
        public string Name;
        public string Value;

        public static IEnumerable<BosunMetaData> DefaultMetaData(BosunMetric metric)
        {
            var hasDescription = !String.IsNullOrEmpty(metric.Description);
            var hasUnit = !String.IsNullOrEmpty(metric.Unit);

            foreach (var suffix in metric.Suffixes)
            {
                var name = metric.Name + suffix;

                yield return new BosunMetaData
                {
                    Metric = name,
                    Name = "rate",
                    Value = metric.MetricType
                };

                if (hasDescription)
                {
                    yield return new BosunMetaData
                    {
                        Metric = name,
                        Name = "desc",
                        Value = metric.Description
                    };
                }

                if (hasUnit)
                {
                    yield return new BosunMetaData
                    {
                        Metric = name,
                        Name = "unit",
                        Value = metric.Unit
                    };
                }
            }
        }
    }
}
