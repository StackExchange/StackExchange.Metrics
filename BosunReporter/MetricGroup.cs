using System;
using System.Collections.Generic;
using System.Reflection;

namespace BosunReporter
{
    public sealed class MetricGroup<T> where T : BosunMetric
    {
        private readonly object _dictionaryLock = new object();
        private readonly MetricsCollector _collector;
        private readonly string _name;
        private readonly Dictionary<string, T> _metrics = new Dictionary<string,T>();
        private readonly Func<string, T> _metricFactory;

        public string Name { get { return _name; } }

        internal MetricGroup(MetricsCollector collector, string name, Func<string, T> metricFactory = null)
        {
            _collector = collector;
            _name = name;
            _metricFactory = metricFactory ?? GetDefaultFactory();
        }

        public T this[string primaryTagValue]
        {
            get
            {
                T metric;
                if (_metrics.TryGetValue(primaryTagValue, out metric))
                    return metric;

                lock (_dictionaryLock)
                {
                    if (_metrics.TryGetValue(primaryTagValue, out metric))
                        return metric;

                    metric = _collector.GetMetric(_name, _metricFactory(primaryTagValue));
                    _metrics[primaryTagValue] = metric;
                    
                    return metric;
                }
            }
        }

        private Func<string, T> GetDefaultFactory()
        {
            // get the constructor which takes a single string argument
            var constructor = typeof(T).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(string) }, null);
            if (constructor == null)
            {

                throw new Exception(
                    String.Format(
                        "Cannot create a MetricGroup for Type \"{0}\". It does not have a constructor which takes a single string argument. " +
                        "Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.",
                        typeof(T).FullName));
            }

            return s => (T)constructor.Invoke(new[] { (object)s });
        }
    }
}
