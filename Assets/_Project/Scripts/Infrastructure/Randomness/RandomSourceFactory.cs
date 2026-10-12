using System;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;

namespace IceP.Infrastructure.Randomness
{
    public static class RandomSourceFactory
    {
        public static IRandomSource Create(DebugRandomSettings settings)
            => Create(settings, () => new SystemRandomSource(), seed => new SystemRandomSource(seed));

        // Construction seam verifies seed isolation without comparing uncontrolled outcomes.
        internal static IRandomSource Create(DebugRandomSettings settings,
            Func<IRandomSource> production, Func<int, IRandomSource> deterministic)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return settings.UseFixedSeed ? deterministic(settings.Seed) : production();
        }
    }
}
