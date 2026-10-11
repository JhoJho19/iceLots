using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IceP.Application;
using IceP.Domain.Configuration;

namespace IceP.Infrastructure.Configuration
{
    public sealed class LocalSlotConfigProvider : ISlotConfigProvider
    {
        private readonly SlotGameConfigSO source;

        public LocalSlotConfigProvider(SlotGameConfigSO source)
        {
            this.source = source;
        }

        public SlotRuntimeConfig Load()
        {
            var errors = new List<string>();
            if (!source) throw new ConfigValidationException(new[] { "SlotGameConfigSO: asset reference is required." });
            if (source.Grid == null) throw new ConfigValidationException(new[] { "Grid: settings are required." });

            var grid = source.Grid;
            var symbols = Map(grid.Symbols, (s, i) =>
            {
                if (s != null) return new SymbolDefinition(s.Id, s.RequiredCountPerReel);
                errors.Add($"Grid.Symbols[{i}]: definition is required.");
                return default;
            });
            var reels = Map(source.ReelStrips, (r, i) => r == null ? null : new ReelStrip(r.Symbols));
            var paylines = Map(source.Paylines, (p, i) => p == null ? null : new Payline(p.Rows));
            var paytable = Map(source.Paytable, (p, i) =>
            {
                if (p != null) return new SymbolPayout(p.Symbol, p.Match3, p.Match4, p.Match5);
                errors.Add($"Paytable[{i}]: entry is required.");
                return default;
            });
            var economy = source.Economy == null ? null : new EconomyConfig(source.Economy.StartingBalance,
                source.Economy.DefaultBet, source.Economy.AvailableBets);
            var scatter = source.Scatter == null ? null : new ScatterRules(
                Map(source.Scatter.Awards, (a, i) =>
                {
                    if (a != null) return new ScatterAward(a.ScatterCount, a.FreeSpins);
                    errors.Add($"Scatter.Awards[{i}]: award is required.");
                    return default;
                }), source.Scatter.FreeSpinsMultiplier, source.Scatter.RetriggerEnabled);
            var daily = source.DailyBonus == null ? null : new DailyBonusRules(source.DailyBonus.Enabled,
                source.DailyBonus.Rewards, TimeSpan.FromSeconds(source.DailyBonus.ResetSecondsUtc),
                source.DailyBonus.StreakResetBehavior);
            var j = source.Jackpot;
            var jackpot = j == null ? null : new JackpotRulesConfig(j.Enabled, j.EventId, j.ConfigVersion,
                j.Amount, j.BaseProbability, j.ReferenceBet, j.MaxWinsPerWeek, j.ResetWeekday,
                TimeSpan.FromSeconds(j.ResetSecondsUtc), ParseUtc(j.StartUtc, "Jackpot.StartUtc", errors),
                ParseUtc(j.EndUtc, "Jackpot.EndUtc", errors));
            var debug = source.DebugRandom == null ? null : new DebugRandomSettings(
                source.DebugRandom.UseFixedSeed, source.DebugRandom.Seed);
            var config = new SlotRuntimeConfig(source.ConfigVersion, grid.ReelCount, grid.VisibleRows,
                grid.ScatterId, symbols, reels, paylines, new Paytable(paytable), economy, scatter, daily, jackpot, debug);
            errors.AddRange(ConfigValidator.Validate(config));
            if (errors.Count > 0) throw new ConfigValidationException(errors);
            return config;
        }

        private static TResult[] Map<TSource, TResult>(TSource[] items, Func<TSource, int, TResult> convert)
            => (items ?? Array.Empty<TSource>()).Select(convert).ToArray();

        private static DateTimeOffset? ParseUtc(string value, string field, List<string> errors)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var formats = new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz" };
            if ((value.EndsWith("Z", StringComparison.Ordinal) || value.EndsWith("+00:00", StringComparison.Ordinal)) &&
                DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var date) && date.Offset == TimeSpan.Zero)
                return date;
            errors.Add(field + ": expected an ISO 8601 UTC timestamp with Z or +00:00, or an empty value.");
            return null;
        }
    }
}
