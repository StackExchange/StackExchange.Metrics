using System;
using System.Collections.Generic;
using System.Reflection;

namespace BosunReporter
{
	internal class MetricGroupTemp<T1, TMetric> where TMetric : BosunMetric
	{
		private readonly MetricsCollector _collector;
		private readonly string _name;
		private readonly Dictionary<T1, TMetric> _metrics = new Dictionary<T1, TMetric>();
		private readonly Func<T1, TMetric> _metricFactory;
		
		internal MetricGroupTemp(MetricsCollector collector, string name, Func<T1, TMetric> metricFactory = null)
		{
			_collector = collector;
			_name = name;
			_metricFactory = metricFactory ?? GetDefaultFactory();
		}

		public TMetric this[T1 tag1]
		{
			get
			{
				TMetric metric;
				if (_metrics.TryGetValue(tag1, out metric))
					return metric;

				// not going to worry about concurrency here because GetMetric is already thread safe, and indempotent.
				metric = _collector.GetMetric(_name, _metricFactory(tag1));
				_metrics[tag1] = metric;

				return metric;
			}
		}

		public Func<T1, TMetric> GetDefaultFactory()
		{
			var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1) }, null);
            if (constructor == null)
            {
				throw new Exception(
					String.Format(
						"Cannot create a MetricGroup for Type \"{0}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
						"Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.",
						typeof(TMetric).FullName));
            }

			return (tag1) => (TMetric)constructor.Invoke(new object[] { tag1 });
		}
	}

	internal class MetricGroupTemp<T1, T2, TMetric> where TMetric : BosunMetric
	{
		private readonly MetricsCollector _collector;
		private readonly string _name;
		private readonly Dictionary<Tuple<T1, T2>, TMetric> _metrics = new Dictionary<Tuple<T1, T2>, TMetric>();
		private readonly Func<T1, T2, TMetric> _metricFactory;
		
		internal MetricGroupTemp(MetricsCollector collector, string name, Func<T1, T2, TMetric> metricFactory = null)
		{
			_collector = collector;
			_name = name;
			_metricFactory = metricFactory ?? GetDefaultFactory();
		}

		public TMetric this[T1 tag1, T2 tag2]
		{
			get
			{
				TMetric metric;
				var key = new Tuple<T1, T2>(tag1, tag2);
				if (_metrics.TryGetValue(key, out metric))
					return metric;

				// not going to worry about concurrency here because GetMetric is already thread safe, and indempotent.
				metric = _collector.GetMetric(_name, _metricFactory(tag1, tag2));
				_metrics[key] = metric;

				return metric;
			}
		}

		public Func<T1, T2, TMetric> GetDefaultFactory()
		{
			var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1), typeof(T2) }, null);
            if (constructor == null)
            {
				throw new Exception(
					String.Format(
						"Cannot create a MetricGroup for Type \"{0}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
						"Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.",
						typeof(TMetric).FullName));
            }

			return (tag1, tag2) => (TMetric)constructor.Invoke(new object[] { tag1, tag2 });
		}
	}

	internal class MetricGroupTemp<T1, T2, T3, TMetric> where TMetric : BosunMetric
	{
		private readonly MetricsCollector _collector;
		private readonly string _name;
		private readonly Dictionary<Tuple<T1, T2, T3>, TMetric> _metrics = new Dictionary<Tuple<T1, T2, T3>, TMetric>();
		private readonly Func<T1, T2, T3, TMetric> _metricFactory;
		
		internal MetricGroupTemp(MetricsCollector collector, string name, Func<T1, T2, T3, TMetric> metricFactory = null)
		{
			_collector = collector;
			_name = name;
			_metricFactory = metricFactory ?? GetDefaultFactory();
		}

		public TMetric this[T1 tag1, T2 tag2, T3 tag3]
		{
			get
			{
				TMetric metric;
				var key = new Tuple<T1, T2, T3>(tag1, tag2, tag3);
				if (_metrics.TryGetValue(key, out metric))
					return metric;

				// not going to worry about concurrency here because GetMetric is already thread safe, and indempotent.
				metric = _collector.GetMetric(_name, _metricFactory(tag1, tag2, tag3));
				_metrics[key] = metric;

				return metric;
			}
		}

		public Func<T1, T2, T3, TMetric> GetDefaultFactory()
		{
			var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1), typeof(T2), typeof(T3) }, null);
            if (constructor == null)
            {
				throw new Exception(
					String.Format(
						"Cannot create a MetricGroup for Type \"{0}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
						"Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.",
						typeof(TMetric).FullName));
            }

			return (tag1, tag2, tag3) => (TMetric)constructor.Invoke(new object[] { tag1, tag2, tag3 });
		}
	}

	internal class MetricGroupTemp<T1, T2, T3, T4, TMetric> where TMetric : BosunMetric
	{
		private readonly MetricsCollector _collector;
		private readonly string _name;
		private readonly Dictionary<Tuple<T1, T2, T3, T4>, TMetric> _metrics = new Dictionary<Tuple<T1, T2, T3, T4>, TMetric>();
		private readonly Func<T1, T2, T3, T4, TMetric> _metricFactory;
		
		internal MetricGroupTemp(MetricsCollector collector, string name, Func<T1, T2, T3, T4, TMetric> metricFactory = null)
		{
			_collector = collector;
			_name = name;
			_metricFactory = metricFactory ?? GetDefaultFactory();
		}

		public TMetric this[T1 tag1, T2 tag2, T3 tag3, T4 tag4]
		{
			get
			{
				TMetric metric;
				var key = new Tuple<T1, T2, T3, T4>(tag1, tag2, tag3, tag4);
				if (_metrics.TryGetValue(key, out metric))
					return metric;

				// not going to worry about concurrency here because GetMetric is already thread safe, and indempotent.
				metric = _collector.GetMetric(_name, _metricFactory(tag1, tag2, tag3, tag4));
				_metrics[key] = metric;

				return metric;
			}
		}

		public Func<T1, T2, T3, T4, TMetric> GetDefaultFactory()
		{
			var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1), typeof(T2), typeof(T3), typeof(T4) }, null);
            if (constructor == null)
            {
				throw new Exception(
					String.Format(
						"Cannot create a MetricGroup for Type \"{0}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
						"Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.",
						typeof(TMetric).FullName));
            }

			return (tag1, tag2, tag3, tag4) => (TMetric)constructor.Invoke(new object[] { tag1, tag2, tag3, tag4 });
		}
	}

	internal class MetricGroupTemp<T1, T2, T3, T4, T5, TMetric> where TMetric : BosunMetric
	{
		private readonly MetricsCollector _collector;
		private readonly string _name;
		private readonly Dictionary<Tuple<T1, T2, T3, T4, T5>, TMetric> _metrics = new Dictionary<Tuple<T1, T2, T3, T4, T5>, TMetric>();
		private readonly Func<T1, T2, T3, T4, T5, TMetric> _metricFactory;
		
		internal MetricGroupTemp(MetricsCollector collector, string name, Func<T1, T2, T3, T4, T5, TMetric> metricFactory = null)
		{
			_collector = collector;
			_name = name;
			_metricFactory = metricFactory ?? GetDefaultFactory();
		}

		public TMetric this[T1 tag1, T2 tag2, T3 tag3, T4 tag4, T5 tag5]
		{
			get
			{
				TMetric metric;
				var key = new Tuple<T1, T2, T3, T4, T5>(tag1, tag2, tag3, tag4, tag5);
				if (_metrics.TryGetValue(key, out metric))
					return metric;

				// not going to worry about concurrency here because GetMetric is already thread safe, and indempotent.
				metric = _collector.GetMetric(_name, _metricFactory(tag1, tag2, tag3, tag4, tag5));
				_metrics[key] = metric;

				return metric;
			}
		}

		public Func<T1, T2, T3, T4, T5, TMetric> GetDefaultFactory()
		{
			var constructor = typeof(TMetric).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new []{ typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5) }, null);
            if (constructor == null)
            {
				throw new Exception(
					String.Format(
						"Cannot create a MetricGroup for Type \"{0}\". It does not have a constructor which matches the signature of types provided to the metric group. " +
						"Either add a constructor with that signature, or use the metricFactory argument to define a custom factory.",
						typeof(TMetric).FullName));
            }

			return (tag1, tag2, tag3, tag4, tag5) => (TMetric)constructor.Invoke(new object[] { tag1, tag2, tag3, tag4, tag5 });
		}
	}

}