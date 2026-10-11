using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.Configuration
{
    public readonly struct ScatterAward
    {
        public int ScatterCount { get; }
        public int FreeSpins { get; }

        public ScatterAward(int scatterCount, int freeSpins)
        {
            ScatterCount = scatterCount;
            FreeSpins = freeSpins;
        }
    }

    public sealed class ScatterRules
    {
        public IReadOnlyList<ScatterAward> Awards { get; }
        public long FreeSpinsMultiplier { get; }
        public bool RetriggerEnabled { get; }

        public ScatterRules(IEnumerable<ScatterAward> awards, long freeSpinsMultiplier, bool retriggerEnabled)
        {
            Awards = Array.AsReadOnly((awards ?? Enumerable.Empty<ScatterAward>()).ToArray());
            FreeSpinsMultiplier = freeSpinsMultiplier;
            RetriggerEnabled = retriggerEnabled;
        }
    }
}
