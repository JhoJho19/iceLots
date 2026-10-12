using System;
using System.Collections.Generic;
using IceP.Domain.Configuration;

namespace IceP.Domain.SlotEngine
{
    /// <summary>Selects one independent uniform stop per configured reel. Reuse this instance
    /// and its injected RNG across outcomes; callers must serialize access.</summary>
    public sealed class OutcomeGenerator
    {
        private readonly IReadOnlyList<ReelStrip> reels;
        private readonly IRandomSource random;

        public OutcomeGenerator(SlotRuntimeConfig config, IRandomSource random)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            ConfigValidator.ValidateAndThrow(config);
            reels = config.ReelStrips;
        }

        public SpinOutcome Generate()
        {
            var stops = new ReelStop[SlotGrid.ReelCount];
            var symbols = new SymbolId[SlotGrid.ReelCount, SlotGrid.RowCount];
            for (var reel = 0; reel < SlotGrid.ReelCount; reel++)
            {
                var strip = reels[reel].Symbols;
                var stop = random.NextInt(0, strip.Count);
                if (stop < 0 || stop >= strip.Count)
                    throw new InvalidOperationException("IRandomSource returned a stop outside the requested range.");
                stops[reel] = new ReelStop(reel, stop);
                for (var row = 0; row < SlotGrid.RowCount; row++)
                    symbols[reel, row] = strip[(stop + row) % strip.Count];
            }
            return new SpinOutcome(new SlotGrid(symbols), stops);
        }
    }
}
