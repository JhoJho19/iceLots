using System;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;

namespace IceP.Infrastructure.Randomness
{
    public static class RandomSourceFactory
    {
        public static IRandomSource Create(DebugRandomSettings settings)
            => Create(settings, () => new SystemRandomSource(), seed => new SystemRandomSource(seed));

        // A distinct stream even on runtimes whose default Random seeds use the
        // same clock tick. Fixed debug seeds remain explicit and reproducible.
        public static IRandomSource CreateJackpot(DebugRandomSettings settings)
            => Create(settings, () => new SystemRandomSource(Guid.NewGuid().GetHashCode()),
                seed => new SystemRandomSource(seed ^ unchecked((int)0x9E3779B9)));

        // Construction seam verifies seed isolation without comparing uncontrolled outcomes.
        internal static IRandomSource Create(DebugRandomSettings settings,
            Func<IRandomSource> production, Func<int, IRandomSource> deterministic)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return settings.UseFixedSeed ? deterministic(settings.Seed) : production();
        }
    }
}
