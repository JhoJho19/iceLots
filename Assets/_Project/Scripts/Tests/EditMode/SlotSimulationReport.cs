using System;
using System.Globalization;
using System.Linq;
using System.Text;
using IceP.Domain.SlotEngine;

namespace IceP.Tests.Simulation
{
    public static class SlotSimulationReport
    {
        private static string F(double value) => value.ToString("G12", CultureInfo.InvariantCulture);
        public static double BinomialStandardError(double p, long n) => n > 0 ? Math.Sqrt(p * (1 - p) / n) : 0;
        // Wilson interval also has meaningful bounds when no rare events are observed.
        public static (double lower, double upper) Wilson(long successes, long trials, double z = 1.96)
        {
            if (trials <= 0 || successes < 0 || successes > trials || z <= 0) throw new ArgumentOutOfRangeException(nameof(trials));
            double p = successes / (double)trials, zz = z * z, divisor = 1 + zz / trials;
            double center = (p + zz / (2 * trials)) / divisor;
            double radius = z * Math.Sqrt(p * (1 - p) / trials + zz / (4 * (double)trials * trials)) / divisor;
            return (Math.Max(0, center - radius), Math.Min(1, center + radius));
        }

        public static (double meanWins, double varianceWins) JackpotExpectation(SlotSimulationResult r)
        {
            if (r.Options.JackpotMode == JackpotSimulationMode.Disabled) return (0, 0);
            var rules = new JackpotRules(r.Config.Jackpot);
            var context = SpinEvaluationContext.Paid(r.Options.Bet);
            if (r.Options.JackpotMode == JackpotSimulationMode.Uncapped)
            {
                double p = rules.CalculateProbability(context, acceptedAtUtc: r.Options.StartUtc);
                return (r.PaidSpins * p, r.PaidSpins * p * (1 - p));
            }
            double expected = 0, variance = 0;
            // Independent analytical capped-binomial DP; probability uses the real rules.
            // Allowance is an absorbing state, not a fresh independent trial after winning.
            int cap = r.Config.Jackpot.MaxWinsPerWeek;
            for (int week = 0; week < r.Options.Weeks; week++)
            {
                var mass = new double[Math.Min(cap, r.Options.PaidSpinsPerWeek) + 1]; mass[0] = 1;
                for (int spin = 0; spin < r.Options.PaidSpinsPerWeek; spin++)
                {
                    double p = rules.CalculateProbability(context, acceptedAtUtc:
                        r.Options.PaidInstant((long)week * r.Options.PaidSpinsPerWeek + spin, r.Config.Jackpot));
                    int last = mass.Length - 1;
                    mass[last] += mass[last - 1] * p;
                    for (int wins = last - 1; wins > 0; wins--) mass[wins] = mass[wins] * (1 - p) + mass[wins - 1] * p;
                    mass[0] *= 1 - p;
                }
                double mean = 0, second = 0;
                for (int wins = 0; wins < mass.Length; wins++) { mean += wins * mass[wins]; second += (double)wins * wins * mass[wins]; }
                expected += mean; variance += Math.Max(0, second - mean * mean);
            }
            return (expected, variance);
        }

        public static string Markdown(SlotSimulationResult r, out bool passed)
        {
            var t = SlotAnalyticalModel.Calculate(r.Config);
            var b = new StringBuilder();
            bool valid = true;
            b.AppendLine($"## {r.PaidSpins:N0} paid spins — {r.Options.JackpotMode}");
            b.AppendLine($"ConfigVersion `{r.Config.ConfigVersion}`; seed `{r.Options.Seed}`; bet `{r.Options.Bet}`; reels `{string.Join(", ", r.Config.ReelStrips.Select(s => s.Symbols.Count))}`; paylines `{r.Config.Paylines.Count}`; FS multiplier `{r.Config.Scatter.FreeSpinsMultiplier}`.");
            b.AppendLine($"Jackpot enabled in config: `{r.Config.Jackpot.Enabled}`; event `{r.Config.Jackpot.EventId}`; amount `{r.Config.Jackpot.Amount}`; base probability `{F(r.Config.Jackpot.BaseProbability)}`; reference bet `{r.Config.Jackpot.ReferenceBet}`; cap `{r.Config.Jackpot.MaxWinsPerWeek}`. Start `{r.Options.StartUtc:O}`; event start `{r.Config.Jackpot.StartUtc:O}`, end `{r.Config.Jackpot.EndUtc:O}` (blank = unbounded).");
            b.AppendLine($"Synthetic weeks `{(r.Options.JackpotMode == JackpotSimulationMode.WeeklyCapped ? r.Options.Weeks : 0)}`, paid spins/week `{(r.Options.JackpotMode == JackpotSimulationMode.WeeklyCapped ? r.Options.PaidSpinsPerWeek : 0)}`; measured elapsed `{F(r.Seconds)}` seconds; paid spins/second `{F(r.PaidSpins / r.Seconds)}`.");
            b.AppendLine();
            b.AppendLine("| Metric (RTP as fraction) | Theory | Measured | Absolute deviation | Relative deviation | N | SE | Approximate 95% interval | 5-SE validation |");
            b.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|---|");
            Action<string, double, RunningMoments> row = (name, expected, samples) =>
            {
                double delta = Math.Abs(samples.Mean - expected), se = samples.StandardError;
                bool ok = samples.Count >= 2 && delta <= 5 * se + 1e-12;
                valid &= ok;
                b.AppendLine($"| {name} | {F(expected)} | {F(samples.Mean)} | {F(delta)} | {(expected == 0 ? "n/a" : F(delta / Math.Abs(expected)))} | {samples.Count} | {F(se)} | [{F(samples.Mean - 1.96 * se)}, {F(samples.Mean + 1.96 * se)}] | {(ok ? "PASS" : "INVESTIGATE")} |");
            };
            row("Base RTP", t.BaseRtp, r.BaseReturns);
            row("Free Spins RTP", t.FreeRtp, r.FreeReturns);
            row("Slot RTP excluding jackpot", t.SlotRtp, r.SlotReturns);
            row("Initial FS/paid spin", t.InitialSpins, r.InitialAwards);
            row("Total FS/paid spin", t.TotalFreeSpins, r.ChainSpins);
            if (r.Options.JackpotMode != JackpotSimulationMode.WeeklyCapped)
            {
                var independentJackpot = JackpotExpectation(r);
                row("Total return including jackpot", t.SlotRtp + independentJackpot.meanWins * r.Config.Jackpot.Amount / r.Wager, r.TotalReturns);
            }
            var j = JackpotExpectation(r);
            double jackpotTheory = j.meanWins * r.Config.Jackpot.Amount / r.Wager;
            double jackpotSe = Math.Sqrt(j.varianceWins) * r.Config.Jackpot.Amount / r.Wager;
            bool jackpotOk = Math.Abs(r.JackpotRtp - jackpotTheory) <= 5 * jackpotSe + 1e-12;
            valid &= jackpotOk;
            b.AppendLine($"| Jackpot RTP (separate) | {F(jackpotTheory)} | {F(r.JackpotRtp)} | {F(Math.Abs(r.JackpotRtp - jackpotTheory))} | {(jackpotTheory == 0 ? "n/a" : F(Math.Abs(r.JackpotRtp - jackpotTheory) / jackpotTheory))} | {(r.Options.JackpotMode == JackpotSimulationMode.WeeklyCapped ? r.Options.Weeks : r.PaidSpins)} | {F(jackpotSe)} | [{F(Math.Max(0, r.JackpotRtp - 1.96 * jackpotSe))}, {F(r.JackpotRtp + 1.96 * jackpotSe)}] (model SE) | {(jackpotOk ? "PASS" : "INVESTIGATE")} |");
            // Paid samples in capped mode share allowance; total-return IID SE would be incorrect.
            b.AppendLine($"\nCombined measured return including jackpot: **{F(r.SlotRtp + r.JackpotRtp)}**; analytical expectation **{F(t.SlotRtp + jackpotTheory)}**. Weekly jackpot uncertainty uses independent weeks and the capped distribution, not IID paid trials.");
            b.AppendLine("\n| Scatter count | Outcomes | Theory | Measured | Absolute deviation | Relative deviation | Binomial SE (theory) | Wilson 95% interval |");
            b.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
            for (int count = 0; count < 6; count++)
            {
                double p = t.ScatterProbabilities[count], empirical = r.ScatterCounts[count] / (double)r.PaidSpins;
                var interval = Wilson(r.ScatterCounts[count], r.PaidSpins);
                var acceptance = Wilson(r.ScatterCounts[count], r.PaidSpins, 5);
                valid &= p >= acceptance.lower - 1e-12 && p <= acceptance.upper + 1e-12;
                b.AppendLine($"| {count} | {r.ScatterCounts[count]} | {F(p)} | {F(empirical)} | {F(Math.Abs(empirical - p))} | {F(Math.Abs(empirical - p) / p)} | {F(BinomialStandardError(p, r.PaidSpins))} | [{F(interval.lower)}, {F(interval.upper)}] |");
            }
            long scatter3 = r.ScatterCounts.Skip(3).Sum();
            var triggerInterval = Wilson(scatter3, r.PaidSpins);
            b.AppendLine($"\nScatter 3+: `{scatter3}` / `{r.PaidSpins}` = `{F(scatter3 / (double)r.PaidSpins)}`; theory `{F(t.TriggerProbability)}`; Wilson 95% `[{F(triggerInterval.lower)}, {F(triggerInterval.upper)}]`.");
            b.AppendLine($"\nPaid wager `{r.Wager}`; base payout `{r.BasePayout}`; free payout `{r.FreePayout}`; monetary winning paid spins `{r.WinningPaidSpins}` ({F(r.WinningPaidSpins / (double)r.PaidSpins)}); winning paid paylines `{r.WinningLines}`; max paid payout `{r.MaxPaidPayout}`; mean paid payout `{F(r.BasePayout / (double)r.PaidSpins)}`.");
            b.AppendLine($"\nBonus chains `{r.BonusTriggers}`; initial FS `{r.InitialFreeSpins}`; executed FS `{r.FreeSpins}`; retriggers `{r.Retriggers}`; retrigger awards `{r.RetriggerAwards}`; longest chain `{r.LongestChain}`; max bonus payout `{r.MaxBonusPayout}`; mean free payout `{F(r.FreeSpins == 0 ? 0 : r.FreePayout / (double)r.FreeSpins)}`.");
            b.AppendLine($"\nJackpot eligible trials `{r.JackpotDraws}`; wins `{r.JackpotWins}`; payout `{r.JackpotPayout}`; RNG calls `{r.JackpotRandomCalls}`; expected wins `{F(j.meanWins)}`; wins SD `{F(Math.Sqrt(j.varianceWins))}`. Uncapped trials are a mathematical reference only; weekly mode applies production Draw/ApplyWin eligibility to a private synthetic profile.");
            b.AppendLine($"\nObserved full-chain return variance `{F(r.SlotReturns.Variance)}`; standard error `{F(r.SlotReturns.StandardError)}`. Each observation includes a paid spin and its complete bonus chain. 95% intervals are approximate estimates, not correctness guarantees. Automated validation uses 5 SE / 5-sigma Wilson bounds to reduce false alarms across multiple metrics; a failure requires investigation, never automatic rebalancing.");
            b.AppendLine("\n### Paid payline and symbol frequencies");
            b.AppendLine("\n| Line (1-based) | Wins | Wins/paid spin |\n|---|---:|---:|");
            for (int line = 0; line < r.PaylineWins.Length; line++) b.AppendLine($"| {line + 1} | {r.PaylineWins[line]} | {F(r.PaylineWins[line] / (double)r.PaidSpins)} |");
            b.AppendLine("\n| Symbol | Wins | Wins/paid spin |\n|---|---:|---:|");
            for (int symbol = 0; symbol < 7; symbol++) b.AppendLine($"| S{symbol + 1} | {r.SymbolWins[symbol]} | {F(r.SymbolWins[symbol] / (double)r.PaidSpins)} |");
            b.AppendLine("\n| Matches | Wins |\n|---|---:|");
            for (int count = 3; count <= 5; count++) b.AppendLine($"| {count} | {r.MatchWins[count]} |");
            b.AppendLine("\n### Paid payout distribution (including zero)\n\n| Coins | Spins |\n|---|---:|");
            foreach (var pair in r.PaidPayoutDistribution) b.AppendLine($"| {pair.Key} | {pair.Value} |");
            b.AppendLine("\n### Completed bonus-chain lengths\n\n| FS | Chains |\n|---|---:|");
            foreach (var pair in r.ChainLengths) b.AppendLine($"| {pair.Key} | {pair.Value} |");
            if (r.WeeklyWins.Count > 0)
            {
                b.AppendLine("\n### Weekly wins\n\n| Week UTC | Paid jackpots |\n|---|---:|");
                foreach (var pair in r.WeeklyWins) b.AppendLine($"| {pair.Key:O} | {pair.Value} |");
                valid &= r.WeeklyWins.Values.All(wins => wins <= r.Config.Jackpot.MaxWinsPerWeek);
            }
            passed = valid;
            b.AppendLine($"\nMathematical validation: **{(valid ? "PASS" : "INVESTIGATE") }**. Daily Bonus excluded.");
            return b.ToString();
        }
    }
}
