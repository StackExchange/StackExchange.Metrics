using System;
using System.Collections.Generic;
using System.Linq;

namespace BosunReporter
{
    public class BosunMetaData
    {
        public string Metric { get; }
        public string Name { get; }
        public string Value { get; }

        public BosunMetaData(string metric, string name, string value)
        {
            Metric = metric;
            Name = name;
            Value = value;
        }
    }
}
