using System.Collections.Generic;

namespace BosunReporter.Infrastructure
{
    internal struct MetricKey
    {
        // Knowing that the lengths and hash codes match should be plenty to ensure uniqueness
        // without holding onto object references.
        internal int NameLength { get; }
        internal int TagsLength { get; }
        internal ulong Hash { get; }

        internal MetricKey(string name, string tagsJson)
        {
            // compute a FNV-1a hash
            const ulong FNV_PRIME = 1099511628211;
            const ulong FNV_OFFSET_BASIS = 14695981039346656037;

            var hash = FNV_OFFSET_BASIS;
            foreach (var c in name)
            {
                hash = unchecked((c ^ hash) * FNV_PRIME);
            }

            foreach (var c in tagsJson)
            {
                hash = unchecked((c ^ hash) * FNV_PRIME);
            }

            NameLength = name.Length;
            TagsLength = tagsJson.Length;
            Hash = hash;
        }
    }

    internal class MetricKeyComparer : IEqualityComparer<MetricKey>
    {
        public bool Equals(MetricKey a, MetricKey b)
        {
            return a.NameLength == b.NameLength
                && a.TagsLength == b.TagsLength
                && a.Hash == b.Hash;
        }

        public int GetHashCode(MetricKey key)
        {
            return unchecked((int)key.Hash);
        }
    }
}