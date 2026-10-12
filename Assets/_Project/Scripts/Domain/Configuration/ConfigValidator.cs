using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.Configuration
{
    public static class ConfigValidator
    {
        public static IReadOnlyList<string> Validate(SlotRuntimeConfig config)
        {
            var errors = new List<string>();
            if (config == null)
            {
                errors.Add("Config: runtime configuration is required.");
                return errors.AsReadOnly();
            }

            if (string.IsNullOrWhiteSpace(config.ConfigVersion)) errors.Add("ConfigVersion: must not be blank.");
            if (config.ReelCount != 5) errors.Add("Grid.ReelCount: version 1 requires 5 reels.");
            if (config.VisibleRows != 3) errors.Add("Grid.VisibleRows: version 1 requires 3 rows.");
            if (config.ScatterId != SymbolId.SCATTER) errors.Add("Grid.ScatterId: must be SCATTER.");

            ValidateSymbolsAndReels(config, errors);
            ValidatePaylinesAndPaytable(config, errors);
            ValidateEconomyAndBonuses(config, errors);
            ValidateJackpot(config.Jackpot, errors);
            if (config.DebugRandom == null) errors.Add("DebugRandom: settings are required.");

            // Only evaluate bounds after structural validation, avoiding misleading arithmetic errors.
            if (errors.Count == 0) ValidateOverflow(config, errors);
            return errors.AsReadOnly();
        }

        public static void ValidateAndThrow(SlotRuntimeConfig config)
        {
            var errors = Validate(config);
            if (errors.Count > 0) throw new ConfigValidationException(errors);
        }

        private static void ValidateSymbolsAndReels(SlotRuntimeConfig config, List<string> errors)
        {
            if (config.Symbols.Count != 8) errors.Add("Symbols: exactly 8 definitions are required.");
            var counts = new Dictionary<SymbolId, int>();
            long total = 0;
            for (var i = 0; i < config.Symbols.Count; i++)
            {
                var symbol = config.Symbols[i];
                if (!Enum.IsDefined(typeof(SymbolId), symbol.Id)) errors.Add($"Symbols[{i}].Id: unknown ID {symbol.Id}.");
                if (counts.ContainsKey(symbol.Id)) errors.Add($"Symbols[{i}].Id: duplicate ID {symbol.Id}.");
                else counts.Add(symbol.Id, symbol.RequiredCountPerReel);
                if (symbol.RequiredCountPerReel <= 0 || symbol.RequiredCountPerReel > 30)
                    errors.Add($"Symbols[{i}].RequiredCountPerReel: must be within [1, 30].");
                total += symbol.RequiredCountPerReel;
            }
            foreach (SymbolId id in Enum.GetValues(typeof(SymbolId)))
                if (!counts.ContainsKey(id)) errors.Add($"Symbols: missing {id}.");
            if (total != 30) errors.Add("Symbols.RequiredCountPerReel: counts must sum to 30.");
            if (counts.TryGetValue(SymbolId.SCATTER, out var scatterCount) && scatterCount != 1)
                errors.Add("Symbols.SCATTER.RequiredCountPerReel: version 1 requires one Scatter per reel.");

            if (config.ReelStrips.Count != 5) errors.Add("ReelStrips: exactly 5 strips are required.");
            var sequences = new HashSet<string>();
            for (var i = 0; i < config.ReelStrips.Count; i++)
            {
                var strip = config.ReelStrips[i];
                if (strip == null) { errors.Add($"ReelStrips[{i}]: strip is required."); continue; }
                if (strip.Symbols.Count != 30) errors.Add($"ReelStrips[{i}].Symbols: exactly 30 positions are required.");
                for (var j = 0; j < strip.Symbols.Count; j++)
                    if (!Enum.IsDefined(typeof(SymbolId), strip.Symbols[j]) || !counts.ContainsKey(strip.Symbols[j]))
                        errors.Add($"ReelStrips[{i}].Symbols[{j}]: unknown symbol {strip.Symbols[j]}.");
                foreach (var pair in counts)
                {
                    var actual = strip.Symbols.Count(id => id == pair.Key);
                    if (actual != pair.Value)
                        errors.Add($"ReelStrips[{i}].Symbols: {pair.Key} count {actual}, expected {pair.Value}.");
                }
                if (!sequences.Add(string.Join(",", strip.Symbols)))
                    errors.Add($"ReelStrips[{i}]: each reel must have a different explicitly stored sequence.");
            }
        }

        private static void ValidatePaylinesAndPaytable(SlotRuntimeConfig config, List<string> errors)
        {
            if (config.Paylines.Count != 20) errors.Add("Paylines: exactly 20 active lines are required.");
            var lines = new HashSet<string>();
            for (var i = 0; i < config.Paylines.Count; i++)
            {
                var line = config.Paylines[i];
                if (line == null) { errors.Add($"Paylines[{i}]: line is required."); continue; }
                if (line.Rows.Count != 5) errors.Add($"Paylines[{i}].Rows: exactly 5 row indices are required.");
                for (var j = 0; j < line.Rows.Count; j++)
                    if (line.Rows[j] < 0 || line.Rows[j] >= config.VisibleRows)
                        errors.Add($"Paylines[{i}].Rows[{j}]: index must be within [0, {config.VisibleRows - 1}].");
                if (!lines.Add(string.Join(",", line.Rows))) errors.Add($"Paylines[{i}]: duplicate line.");
            }

            if (config.Paytable == null) { errors.Add("Paytable: entries are required."); return; }
            if (config.Paytable.Entries.Count != 7) errors.Add("Paytable.Entries: exactly 7 ordinary symbol entries are required.");
            var symbols = new HashSet<SymbolId>();
            for (var i = 0; i < config.Paytable.Entries.Count; i++)
            {
                var entry = config.Paytable.Entries[i];
                if (!Enum.IsDefined(typeof(SymbolId), entry.Symbol) || entry.Symbol == SymbolId.SCATTER)
                    errors.Add($"Paytable.Entries[{i}].Symbol: must be S1 through S7; Scatter has no line payout.");
                if (!symbols.Add(entry.Symbol)) errors.Add($"Paytable.Entries[{i}].Symbol: duplicate entry.");
                if (entry.Match3 < 0) errors.Add($"Paytable.Entries[{i}].Match3: must be non-negative.");
                if (entry.Match4 < 0) errors.Add($"Paytable.Entries[{i}].Match4: must be non-negative.");
                if (entry.Match5 < 0) errors.Add($"Paytable.Entries[{i}].Match5: must be non-negative.");
            }
            for (var i = 0; i < 7; i++)
                if (!symbols.Contains((SymbolId)i)) errors.Add($"Paytable.Entries: missing {(SymbolId)i}.");
        }

        private static void ValidateEconomyAndBonuses(SlotRuntimeConfig config, List<string> errors)
        {
            var economy = config.Economy;
            if (economy == null) errors.Add("Economy: settings are required.");
            else
            {
                if (economy.StartingBalance < 0) errors.Add("Economy.StartingBalance: must be non-negative.");
                if (economy.AvailableBets.Count == 0) errors.Add("Economy.AvailableBets: at least one bet is required.");
                var bets = new HashSet<long>();
                for (var i = 0; i < economy.AvailableBets.Count; i++)
                {
                    var bet = economy.AvailableBets[i];
                    if (bet <= 0) errors.Add($"Economy.AvailableBets[{i}]: must be positive.");
                    if (config.Paylines.Count > 0 && bet % config.Paylines.Count != 0)
                        errors.Add($"Economy.AvailableBets[{i}]: must be divisible by {config.Paylines.Count} active paylines.");
                    if (!bets.Add(bet)) errors.Add($"Economy.AvailableBets[{i}]: duplicate bet.");
                }
                if (!bets.Contains(economy.DefaultBet)) errors.Add("Economy.DefaultBet: must be an available bet.");
            }

            var scatter = config.Scatter;
            if (scatter == null) errors.Add("Scatter: settings are required.");
            else
            {
                if (scatter.FreeSpinsMultiplier <= 0) errors.Add("Scatter.FreeSpinsMultiplier: must be positive.");
                if (scatter.Awards.Count != 3) errors.Add("Scatter.Awards: thresholds 3, 4 and 5 are required.");
                var thresholds = new HashSet<int>();
                for (var i = 0; i < scatter.Awards.Count; i++)
                {
                    var award = scatter.Awards[i];
                    if (award.ScatterCount < 3 || award.ScatterCount > 5)
                        errors.Add($"Scatter.Awards[{i}].ScatterCount: must be 3, 4 or 5.");
                    if (!thresholds.Add(award.ScatterCount)) errors.Add($"Scatter.Awards[{i}].ScatterCount: duplicate threshold.");
                    if (award.FreeSpins < 0) errors.Add($"Scatter.Awards[{i}].FreeSpins: must be non-negative.");
                }
                for (var i = 3; i <= 5; i++)
                    if (!thresholds.Contains(i)) errors.Add($"Scatter.Awards: missing threshold {i}.");
            }

            var daily = config.DailyBonus;
            if (daily == null) errors.Add("DailyBonus: settings are required.");
            else
            {
                if (daily.Rewards.Count != 7) errors.Add("DailyBonus.Rewards: exactly 7 daily rewards are required.");
                for (var i = 0; i < daily.Rewards.Count; i++)
                    if (daily.Rewards[i] < 0) errors.Add($"DailyBonus.Rewards[{i}]: must be non-negative.");
                ValidateTime(daily.ResetTimeUtc, "DailyBonus.ResetTimeUtc", errors);
                if (!Enum.IsDefined(typeof(StreakResetBehavior), daily.StreakResetBehavior))
                    errors.Add("DailyBonus.StreakResetBehavior: unknown policy.");
            }
        }

        private static void ValidateJackpot(JackpotRulesConfig jackpot, List<string> errors)
        {
            if (jackpot == null) { errors.Add("Jackpot: settings are required."); return; }
            if (string.IsNullOrWhiteSpace(jackpot.EventId)) errors.Add("Jackpot.EventId: must not be blank.");
            if (string.IsNullOrWhiteSpace(jackpot.ConfigVersion)) errors.Add("Jackpot.ConfigVersion: must not be blank.");
            if (jackpot.Amount < 0) errors.Add("Jackpot.Amount: must be non-negative.");
            if (double.IsNaN(jackpot.BaseProbability) || double.IsInfinity(jackpot.BaseProbability) ||
                jackpot.BaseProbability < 0 || jackpot.BaseProbability > 1)
                errors.Add("Jackpot.BaseProbability: must be finite and within [0, 1].");
            if (jackpot.ReferenceBet <= 0) errors.Add("Jackpot.ReferenceBet: must be positive.");
            if (jackpot.MaxWinsPerWeek != 1) errors.Add("Jackpot.MaxWinsPerWeek: version 1 requires a limit of 1.");
            if (!Enum.IsDefined(typeof(DayOfWeek), jackpot.ResetWeekday)) errors.Add("Jackpot.ResetWeekday: unknown weekday.");
            ValidateTime(jackpot.ResetTimeUtc, "Jackpot.ResetTimeUtc", errors);
            if (jackpot.StartUtc.HasValue && jackpot.StartUtc.Value.Offset != TimeSpan.Zero)
                errors.Add("Jackpot.StartUtc: must use UTC (zero offset).");
            if (jackpot.EndUtc.HasValue && jackpot.EndUtc.Value.Offset != TimeSpan.Zero)
                errors.Add("Jackpot.EndUtc: must use UTC (zero offset).");
            if (jackpot.StartUtc.HasValue && jackpot.EndUtc.HasValue && jackpot.StartUtc >= jackpot.EndUtc)
                errors.Add("Jackpot.EndUtc: must be later than StartUtc.");
        }

        private static void ValidateTime(TimeSpan time, string field, List<string> errors)
        {
            if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) errors.Add(field + ": must be within one UTC day.");
        }

        private static void ValidateOverflow(SlotRuntimeConfig config, List<string> errors)
        {
            try
            {
                var maxBet = config.Economy.AvailableBets.Max();
                var maxMultiplier = config.Paytable.Entries.Max(e => Math.Max(e.Match3, Math.Max(e.Match4, e.Match5)));
                checked
                {
                    var allLineWin = (maxBet / config.Paylines.Count) * maxMultiplier * config.Paylines.Count;
                    var freeSpinWin = allLineWin * config.Scatter.FreeSpinsMultiplier;
                    var paidWinWithJackpot = allLineWin + config.Jackpot.Amount;
                    var maxSettlement = Math.Max(freeSpinWin, paidWinWithJackpot);
                    // Configured starting balance plus one maximum settlement and one daily reward.
                    // Later transactions must still use checked arithmetic on the actual balance.
                    var balanceBound = config.Economy.StartingBalance + maxSettlement + config.DailyBonus.Rewards.Max();
                    if (balanceBound < config.Economy.StartingBalance)
                        errors.Add("PayoutBounds: maximum configured balance is below the starting balance.");
                }
            }
            catch (OverflowException)
            {
                errors.Add("PayoutBounds: available bets, paytable, FreeSpinsMultiplier, Jackpot.Amount or starting balance/rewards can overflow Int64.");
            }
        }
    }
}
