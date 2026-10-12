using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.Configuration
{
    public sealed class DebugRandomSettings
    {
        public bool UseFixedSeed { get; }
        public int Seed { get; }

        public DebugRandomSettings(bool useFixedSeed, int seed)
        {
            UseFixedSeed = useFixedSeed;
            Seed = seed;
        }
    }

    // All nested DTOs are immutable. A future spin/bonus chain can retain this exact snapshot.
    public sealed class SlotRuntimeConfig
    {
        public string ConfigVersion { get; }
        public int ReelCount { get; }
        public int VisibleRows { get; }
        public SymbolId ScatterId { get; }
        public IReadOnlyList<SymbolDefinition> Symbols { get; }
        public IReadOnlyList<ReelStrip> ReelStrips { get; }
        public IReadOnlyList<Payline> Paylines { get; }
        public Paytable Paytable { get; }
        public EconomyConfig Economy { get; }
        public ScatterRules Scatter { get; }
        public DailyBonusRules DailyBonus { get; }
        public JackpotRulesConfig Jackpot { get; }
        public DebugRandomSettings DebugRandom { get; }

        public SlotRuntimeConfig(string configVersion, int reelCount, int visibleRows, SymbolId scatterId,
            IEnumerable<SymbolDefinition> symbols, IEnumerable<ReelStrip> reelStrips,
            IEnumerable<Payline> paylines, Paytable paytable, EconomyConfig economy,
            ScatterRules scatter, DailyBonusRules dailyBonus, JackpotRulesConfig jackpot,
            DebugRandomSettings debugRandom)
        {
            ConfigVersion = configVersion;
            ReelCount = reelCount;
            VisibleRows = visibleRows;
            ScatterId = scatterId;
            Symbols = Array.AsReadOnly((symbols ?? Enumerable.Empty<SymbolDefinition>()).ToArray());
            ReelStrips = Array.AsReadOnly((reelStrips ?? Enumerable.Empty<ReelStrip>()).ToArray());
            Paylines = Array.AsReadOnly((paylines ?? Enumerable.Empty<Payline>()).ToArray());
            Paytable = paytable;
            Economy = economy;
            Scatter = scatter;
            DailyBonus = dailyBonus;
            Jackpot = jackpot;
            DebugRandom = debugRandom;
        }
    }
}
