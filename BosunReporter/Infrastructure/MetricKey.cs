using System.Collections.Generic;

namespace BosunReporter.Infrastructure
{
    readonly struct MetricKey
    {
        // Knowing that the lengths and hash codes match should be plenty to ensure uniqueness
        // without holding onto object references.
        internal int NameLength { get; }
        internal int TagsLength { get; }
        internal ulong Hash { get; }

        internal MetricKey(string name, IReadOnlyDictionary<string, string> tags)
        {
            // compute a FNV-1a hash
            const ulong FNV_PRIME = 1099511628211;
            const ulong FNV_OFFSET_BASIS = 14695981039346656037;

            var hash = FNV_OFFSET_BASIS;
            foreach (var c in name)
            {
                hash = unchecked((c ^ hash) * FNV_PRIME);
            }

            TagsLength = 0;
            if (tags != null)
            {
                foreach (var tagNameAndValue in tags)
                {
                    var tag = tagNameAndValue.Key + ":" + tagNameAndValue.Value;
                    foreach (var c in tag)
                    {
                        hash = unchecked((c ^ hash) * FNV_PRIME);
                    }

                    TagsLength += tag.Length;
                }
            }

            NameLength = name.Length;
            Hash = hash;
        }
    }

    class MetricKeyComparer : IEqualityComparer<MetricKey>
    {
        private MetricKeyComparer()
        {
        }

        public static readonly MetricKeyComparer Default = new MetricKeyComparer();

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