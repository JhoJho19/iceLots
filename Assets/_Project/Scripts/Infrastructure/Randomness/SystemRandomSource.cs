using System;
using IceP.Domain.SlotEngine;

namespace IceP.Infrastructure.Randomness
{
    /// <summary>Owns one System.Random for its lifetime. Seeded sequences are reproducible
    /// on the same runtime; this is a local gameplay RNG, not a cryptographic RNG.</summary>
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random random;

        public SystemRandomSource() { random = new Random(); }
        public SystemRandomSource(int seed) { random = new Random(seed); }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "The range must be nonempty.");
            return random.Next(minInclusive, maxExclusive);
        }
    }
}
