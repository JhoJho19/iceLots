using System;
using System.Collections.Generic;
using System.Linq;
using IceP.Domain.Configuration;

namespace IceP.Domain.SlotEngine
{
    public sealed class WinEvaluator
    {
        private readonly SlotRuntimeConfig config;
        private readonly IReadOnlyDictionary<SymbolId, SymbolPayout> payouts;

        public WinEvaluator(SlotRuntimeConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidator.ValidateAndThrow(config);
            payouts = config.Paytable.Entries.ToDictionary(p => p.Symbol);
        }

        internal void ValidateBet(long totalBet)
        {
            if (totalBet <= 0 || totalBet % config.Paylines.Count != 0 ||
                !config.Economy.AvailableBets.Contains(totalBet))
                throw new ArgumentOutOfRangeException(nameof(totalBet), "Bet must be configured, positive and divisible by the active line count.");
        }

        public WinEvaluation Evaluate(SlotGrid grid, long totalBet)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            ValidateBet(totalBet);
            var lineBet = totalBet / config.Paylines.Count;
            var wins = new List<LineWin>();
            long total = 0;
            for (var index = 0; index < config.Paylines.Count; index++)
            {
                var rows = config.Paylines[index].Rows;
                var symbol = grid[0, rows[0]];
                if (symbol == config.ScatterId) continue;
                var count = 1;
                while (count < SlotGrid.ReelCount && grid[count, rows[count]] == symbol) count++;
                if (count < 3) continue;
                var entry = payouts[symbol];
                var multiplier = count == 5 ? entry.Match5 : count == 4 ? entry.Match4 : entry.Match3;
                if (multiplier == 0) continue;
                var payout = checked(lineBet * multiplier);
                total = checked(total + payout);
                wins.Add(new LineWin(index, symbol, count, multiplier, payout,
                    Enumerable.Range(0, count).Select(reel => new GridCoordinate(reel, rows[reel]))));
            }
            return new WinEvaluation(wins, total);
        }
    }
}
