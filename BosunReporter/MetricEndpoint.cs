using BosunReporter.Infrastructure;
using System;
using System.Collections.Generic;

namespace BosunReporter
{
    /// <summary>
    /// Represents an endpoint to send metric data in a particular format to.
    /// </summary>
    public class MetricEndpoint
    {
        /// <summary>
        /// Constructs a new endpoint with the specific name and <see cref="IMetricHandler"/>.
        /// </summary>
        /// <param name="name">
        /// Name of the endpoint.
        /// </param>
        /// <param name="handler">
        /// An <see cref="IMetricHandler"/> used to serialize and metrics.
        /// </param>
        public MetricEndpoint(string name, IMetricHandler handler)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Gets the name of the sink for display purposes.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets an <see cref="IMetricHandler"/> used to serialize and send metrics to the endpoint.
        /// </summary>
        public IMetricHandler Handler { get; }
    }

    class MetricEndpointComparer : IEqualityComparer<MetricEndpoint>
    {
        public static MetricEndpointComparer Default { get; } = new MetricEndpointComparer();

        private MetricEndpointComparer()
        {
        }

        public bool Equals(MetricEndpoint a, MetricEndpoint b)
        {
            return a.Name == b.Name && ReferenceEquals(a.Handler, b.Handler);
        }

        public int GetHashCode(MetricEndpoint endpoint)
        {
            return endpoint.Name.GetHashCode() ^ endpoint.Handler.GetHashCode();
        }
    }
}
