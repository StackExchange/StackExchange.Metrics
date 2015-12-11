namespace BosunReporter.Infrastructure
{
    internal struct MetricKey
    {
        // Knowing that the lengths and hash codes match should be plenty to ensure uniqueness
        // without holding onto object references.
        private short NameLength;
        private short TagsLength;
        private ulong Hash;

        internal static MetricKey GetKey(string name, byte[] tagsJson)
        {
            // compute a FNV-1a hash
            const ulong FNV_PRIME = 1099511628211;
            const ulong FNV_OFFSET_BASIS = 14695981039346656037;

            ulong hash = FNV_OFFSET_BASIS;
            foreach (var c in name)
            {
                hash = unchecked((c ^ hash) * FNV_PRIME);
            }

            foreach (var b in tagsJson)
            {
                hash = unchecked((b ^ hash) * FNV_PRIME);
            }

            return new MetricKey
            {
                NameLength = (short)name.Length,
                TagsLength = (short)tagsJson.Length,
                Hash = hash,
            };
        }
    }
}