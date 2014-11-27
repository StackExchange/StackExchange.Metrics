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
            return metric.Suffixes.Select(suffix => new BosunMetaData
            {
                Metric = metric.Name + suffix,
                Name = "rate",
                Value = metric.MetricType
            });
        }
    }
}
