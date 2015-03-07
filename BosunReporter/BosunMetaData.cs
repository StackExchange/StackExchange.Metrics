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
            foreach (var suffix in metric.Suffixes)
            {
                var name = metric.Name + suffix;

                yield return new BosunMetaData
                {
                    Metric = name,
                    Name = "rate",
                    Value = metric.MetricType
                };

                var desc = metric.GetDescription(suffix);
                if (!String.IsNullOrEmpty(desc))
                {
                    yield return new BosunMetaData
                    {
                        Metric = name,
                        Name = "desc",
                        Value = desc
                    };
                }

                var unit = metric.GetUnit(suffix);
                if (!String.IsNullOrEmpty(unit))
                {
                    yield return new BosunMetaData
                    {
                        Metric = name,
                        Name = "unit",
                        Value = unit
                    };
                }
            }
        }
    }
}
