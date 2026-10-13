using System;
using System.Linq;
using IceP.Domain.Configuration;

namespace IceP.Tests.Simulation
{
    public sealed class SlotAnalyticalModel
    {
        public double BaseRtp, FreeRtp, SlotRtp, TriggerProbability, InitialSpins, TotalFreeSpins;
        public double[] ScatterProbabilities;
        // Probability of each exact 3/4/5 symbol match on each configured line.
        public double[,,] LineSymbolMatches;
        public static SlotAnalyticalModel Calculate(SlotRuntimeConfig config)
        {
            ConfigValidator.ValidateAndThrow(config);
            var result = new SlotAnalyticalModel { LineSymbolMatches = new double[config.Paylines.Count, 7, 6] };
            for (int lineIndex = 0; lineIndex < config.Paylines.Count; lineIndex++)
            {
                var line = config.Paylines[lineIndex];
                foreach (var entry in config.Paytable.Entries)
                {
                    var p = Enumerable.Range(0, config.ReelCount).Select(reel =>
                    {
                        var strip = config.ReelStrips[reel].Symbols;
                        return Enumerable.Range(0, strip.Count).Count(stop =>
                            strip[(stop + line.Rows[reel]) % strip.Count] == entry.Symbol) / (double)strip.Count;
                    }).ToArray();
                    double prefix = p[0] * p[1] * p[2];
                    var exact = new[] { prefix * (1 - p[3]), prefix * p[3] * (1 - p[4]), prefix * p[3] * p[4] };
                    for (int count = 3; count <= 5; count++) result.LineSymbolMatches[lineIndex, (int)entry.Symbol, count] = exact[count - 3];
                    result.BaseRtp += (exact[0] * entry.Match3 + exact[1] * entry.Match4 + exact[2] * entry.Match5) / config.Paylines.Count;
                }
            }
            var distribution = new[] { 1.0 };
            foreach (var reel in config.ReelStrips)
            {
                var marginal = new double[config.VisibleRows + 1];
                for (int stop = 0; stop < reel.Symbols.Count; stop++)
                {
                    int count = 0;
                    for (int row = 0; row < config.VisibleRows; row++)
                        if (reel.Symbols[(stop + row) % reel.Symbols.Count] == config.ScatterId) count++;
                    marginal[count] += 1.0 / reel.Symbols.Count;
                }
                var next = new double[distribution.Length + marginal.Length - 1];
                for (int a = 0; a < distribution.Length; a++)
                    for (int b = 0; b < marginal.Length; b++) next[a + b] += distribution[a] * marginal[b];
                distribution = next;
            }
            result.ScatterProbabilities = distribution;
            foreach (var award in config.Scatter.Awards)
            {
                result.InitialSpins += distribution[award.ScatterCount] * award.FreeSpins;
                if (award.FreeSpins > 0) result.TriggerProbability += distribution[award.ScatterCount];
            }
            double reproduction = config.Scatter.RetriggerEnabled ? result.InitialSpins : 0;
            if (reproduction >= 1) throw new InvalidOperationException("Expected bonus-chain size does not converge.");
            result.TotalFreeSpins = result.InitialSpins / (1 - reproduction);
            result.FreeRtp = result.BaseRtp * result.TotalFreeSpins * config.Scatter.FreeSpinsMultiplier;
            result.SlotRtp = result.BaseRtp + result.FreeRtp;
            return result;
        }
    }
}
