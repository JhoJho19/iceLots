using System;
using System.IO;
using System.Linq;
using System.Text;
using IceP.Application;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using IceP.Tests.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace IceP.Tests
{
    public sealed class SlotSimulationTests
    {
        private SlotRuntimeConfig config;
        public static SlotRuntimeConfig LoadConfig() => new LocalSlotConfigProvider(
            AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
        [SetUp] public void Setup() { config = LoadConfig(); }

        private sealed class ScriptedReels : IRandomSource
        {
            private readonly int[][] spins;
            public int Calls;
            public ScriptedReels(params int[][] spins) { this.spins = spins; }
            public int NextInt(int min, int max)
            {
                int call = Calls++;
                return spins[Math.Min(call / 5, spins.Length - 1)][call % 5];
            }
        }
        private sealed class Bits : IRandomSource
        {
            public int Calls;
            public int NextInt(int min, int max) { Calls++; return 0; }
        }
        private int[] ScatterStops(bool scatter) => config.ReelStrips.Select(strip =>
            Enumerable.Range(0, strip.Symbols.Count).First(stop =>
                Enumerable.Range(0, 3).Any(row => strip.Symbols[(stop + row) % strip.Symbols.Count] == config.ScatterId) == scatter)).ToArray();
        private SlotRuntimeConfig WithRules(long multiplier = 1, bool retrigger = true, bool zeroPay = false,
            bool enabled = true, double probability = .0001, int cap = 1, DateTimeOffset? start = null, DateTimeOffset? end = null)
        {
            var j = config.Jackpot;
            return new SlotRuntimeConfig(config.ConfigVersion, 5, 3, config.ScatterId, config.Symbols,
                config.ReelStrips, config.Paylines, zeroPay ? new Paytable(config.Paytable.Entries.Select(p =>
                    new SymbolPayout(p.Symbol, 0, 0, 0))) : config.Paytable, config.Economy,
                new ScatterRules(config.Scatter.Awards, multiplier, retrigger), config.DailyBonus,
                new JackpotRulesConfig(enabled, j.EventId, j.ConfigVersion, j.Amount, probability, j.ReferenceBet,
                    cap, j.ResetWeekday, j.ResetTimeUtc, start, end), config.DebugRandom);
        }

        [Test]
        public void SpecifiedRngHasCrossRuntimeGoldenSequence()
        {
            var rng = new SimulationRandom(0);
            Assert.That(new[] { rng.NextInt(0, 65536), rng.NextInt(0, 65536), rng.NextInt(0, 65536) },
                Is.EqualTo(new[] { 52655, 26100, 17743 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(2, 2));
        }
        [Test]
        public void SameSeedHasIdenticalAggregateAndDifferentSeedChangesOutcomes()
        {
            var options = new SlotSimulationOptions(1000);
            var a = SlotSimulationRunner.Run(config, options);
            var b = SlotSimulationRunner.Run(config, options);
            Assert.That(b.BasePayout, Is.EqualTo(a.BasePayout));
            Assert.That(b.FreePayout, Is.EqualTo(a.FreePayout));
            Assert.That(b.ScatterCounts, Is.EqualTo(a.ScatterCounts));
            Assert.That(b.PaylineWins, Is.EqualTo(a.PaylineWins));
            Assert.That(b.PaidPayoutDistribution, Is.EqualTo(a.PaidPayoutDistribution));
            Assert.That(b.ChainLengths, Is.EqualTo(a.ChainLengths));
            Assert.That(b.SlotReturns.Mean, Is.EqualTo(a.SlotReturns.Mean));
            Assert.That(b.SlotReturns.Variance, Is.EqualTo(a.SlotReturns.Variance));
            var other = SlotSimulationRunner.Run(config, new SlotSimulationOptions(1000, seed: 413));
            Assert.That(other.PaidPayoutDistribution, Is.Not.EqualTo(a.PaidPayoutDistribution));
        }
        [Test]
        public void TenThousandSpinSmokeUsesActualConfigurationAndReportsMeasuredResults()
        {
            var assetBefore = File.ReadAllBytes("Assets/_Project/Configs/SlotGameConfigSO.asset");
            var stripsBefore = config.ReelStrips.Select(s => s.Symbols.ToArray()).ToArray();
            var result = SlotSimulationRunner.Run(config, new SlotSimulationOptions(10000));
            Assert.That(result.Wager, Is.EqualTo(10000000));
            Assert.That(result.SlotReturns.Count, Is.EqualTo(10000));
            Assert.That(result.ScatterCounts.Sum(), Is.EqualTo(10000));
            Assert.That(result.PaidPayoutDistribution.Values.Sum(), Is.EqualTo(10000));
            Assert.That(result.FreeSpins, Is.EqualTo(result.InitialFreeSpins + result.RetriggerAwards));
            Assert.That(result.WinningLines, Is.EqualTo(result.PaylineWins.Sum()));
            // Measured once with the specified SplitMix64 algorithm; catches reel-order/paytable regressions.
            Assert.That(result.BasePayout, Is.EqualTo(8649950));
            Assert.That(result.FreePayout, Is.EqualTo(613050));
            Assert.That(result.FreeSpins, Is.EqualTo(788));
            Assert.That(result.ScatterCounts, Is.EqualTo(new long[] { 5927, 3229, 759, 80, 5, 0 }));
            Assert.That(result.WinningLines, Is.EqualTo(4655));
            for (int reel = 0; reel < 5; reel++) Assert.That(config.ReelStrips[reel].Symbols, Is.EqualTo(stripsBefore[reel]));
            Assert.That(File.ReadAllBytes("Assets/_Project/Configs/SlotGameConfigSO.asset"), Is.EqualTo(assetBefore));
            Assert.That(SlotSimulationReport.Markdown(result, out var passed), Does.Contain("Daily Bonus excluded"));
            Assert.That(passed, Is.True);
            TestContext.WriteLine($"Golden smoke: base={result.BasePayout}, free={result.FreePayout}, FS={result.FreeSpins}, scatter={string.Join(",", result.ScatterCounts)}, wins={result.WinningLines}");
        }
        [Test]
        public void PaidAccountingAndEveryLineMatchDirectEngineWithLossesInDenominator()
        {
            var stops = ScatterStops(false);
            var expected = new SlotEngine(config, new ScriptedReels(stops)).Generate(SpinEvaluationContext.Paid(600));
            var result = SlotSimulationRunner.Run(config, new SlotSimulationOptions(3, 600), new ScriptedReels(stops));
            Assert.That(result.BasePayout, Is.EqualTo(3 * expected.FinalPayout));
            Assert.That(result.Wager, Is.EqualTo(1800));
            Assert.That(result.FreeSpins, Is.Zero);
            for (int line = 0; line < 20; line++)
                Assert.That(result.PaylineWins[line], Is.EqualTo(3 * expected.Wins.WinningLines.Count(w => w.PaylineIndex == line)));
            var loss = SlotSimulationRunner.Run(WithRules(zeroPay: true), new SlotSimulationOptions(3), new ScriptedReels(stops));
            Assert.That(loss.Wager, Is.EqualTo(3000)); Assert.That(loss.SlotRtp, Is.Zero);
            Assert.That(loss.SlotReturns.Count, Is.EqualTo(3)); Assert.That(loss.SlotReturns.Variance, Is.Zero);
        }
        [Test]
        public void FreeQueueUsesTriggerBetMultiplierAndCompletesRetriggerBeforeObservation()
        {
            var scatter = ScatterStops(true); var ordinary = ScatterStops(false);
            var rules = WithRules(multiplier: 3);
            var expected = new SlotEngine(rules, new ScriptedReels(ordinary)).Generate(SpinEvaluationContext.Free(400));
            var scatterFree = new SlotEngine(rules, new ScriptedReels(scatter)).Generate(SpinEvaluationContext.Free(400));
            var result = SlotSimulationRunner.Run(rules, new SlotSimulationOptions(1, 400), new ScriptedReels(scatter, scatter, ordinary));
            Assert.That(result.Wager, Is.EqualTo(400));
            Assert.That(result.InitialFreeSpins, Is.EqualTo(20));
            Assert.That(result.Retriggers, Is.EqualTo(1)); Assert.That(result.RetriggerAwards, Is.EqualTo(20));
            Assert.That(result.FreeSpins, Is.EqualTo(40));
            Assert.That(result.FreePayout, Is.EqualTo(scatterFree.FinalPayout + 39 * expected.FinalPayout));
            Assert.That(result.FreeRtp, Is.EqualTo(result.FreePayout / 400.0));
            Assert.That(result.ChainLengths[40], Is.EqualTo(1));
            Assert.That(result.SlotReturns.Count, Is.EqualTo(1));
            Assert.That(result.SlotReturns.Mean, Is.EqualTo(result.SlotRtp));
            Assert.That(expected.FinalPayout, Is.EqualTo(expected.Wins.BaseWin * 3));
        }
        [Test]
        public void DisabledRetriggerFinishesInitialQueue()
        {
            var r = SlotSimulationRunner.Run(WithRules(retrigger: false), new SlotSimulationOptions(1), new ScriptedReels(ScatterStops(true)));
            Assert.That(r.FreeSpins, Is.EqualTo(20)); Assert.That(r.Retriggers, Is.Zero);
            Assert.That(SlotAnalyticalModel.Calculate(r.Config).TotalFreeSpins, Is.EqualTo(.0704).Within(1e-12));
        }
        [Test]
        public void GuardFailsInsteadOfReturningTruncatedRtp()
        {
            Assert.Throws<InvalidOperationException>(() => SlotSimulationRunner.Run(config,
                new SlotSimulationOptions(1, maxBonusChain: 30), new ScriptedReels(ScatterStops(true))));
        }
        [Test]
        public void AnalyticalModelDerivesDocumentedExactExpectationsAndAllLineProbabilities()
        {
            var t = SlotAnalyticalModel.Calculate(config);
            Assert.That(t.BaseRtp, Is.EqualTo(.85605855967).Within(1e-11));
            Assert.That(t.FreeRtp, Is.EqualTo(.06483059660).Within(1e-11));
            Assert.That(t.SlotRtp, Is.EqualTo(.92088915627).Within(1e-11));
            Assert.That(t.ScatterProbabilities.Sum(), Is.EqualTo(1).Within(1e-12));
            Assert.That(t.TriggerProbability, Is.EqualTo(.00856).Within(1e-12));
            Assert.That(t.InitialSpins, Is.EqualTo(.0704).Within(1e-12));
            Assert.That(t.TotalFreeSpins, Is.EqualTo(.0704 / (1 - .0704)).Within(1e-12));
            for (int line = 0; line < 20; line++)
            {
                Assert.That(t.LineSymbolMatches[line, 0, 3], Is.EqualTo(.2 * .2 * .2 * .8).Within(1e-12));
                Assert.That(t.LineSymbolMatches[line, 0, 4], Is.EqualTo(Math.Pow(.2, 4) * .8).Within(1e-12));
                Assert.That(t.LineSymbolMatches[line, 0, 5], Is.EqualTo(Math.Pow(.2, 5)).Within(1e-12));
            }
        }
        [Test]
        public void ScatterAnalyticalWindowsAreNotFifteenIndependentCells()
        {
            var t = SlotAnalyticalModel.Calculate(config);
            Assert.That(t.ScatterProbabilities[0], Is.EqualTo(Math.Pow(.9, 5)).Within(1e-12));
            Assert.That(t.ScatterProbabilities.Skip(6).Sum(), Is.Zero);
            Assert.That(t.ScatterProbabilities[5], Is.EqualTo(.00001).Within(1e-12));
        }
        [TestCase(JackpotSimulationMode.Uncapped)]
        [TestCase(JackpotSimulationMode.WeeklyCapped)]
        public void JackpotNeverDrawsForFreeSpins(JackpotSimulationMode mode)
        {
            var bits = new Bits();
            var r = SlotSimulationRunner.Run(config, new SlotSimulationOptions(1, jackpotMode: mode, weeks: 1, paidSpinsPerWeek: 1),
                new ScriptedReels(ScatterStops(true), ScatterStops(false)), bits);
            Assert.That(r.FreeSpins, Is.EqualTo(20)); Assert.That(r.JackpotDraws, Is.EqualTo(1));
            Assert.That(r.JackpotWins, Is.EqualTo(1)); Assert.That(bits.Calls, Is.EqualTo(14));
        }
        [Test]
        public void WeeklyCapStopsRandomDrawsAndNextWeekRestoresAllowance()
        {
            var bits = new Bits();
            var r = SlotSimulationRunner.Run(config, new SlotSimulationOptions(6, jackpotMode: JackpotSimulationMode.WeeklyCapped,
                weeks: 2, paidSpinsPerWeek: 3), new ScriptedReels(ScatterStops(false)), bits);
            Assert.That(r.JackpotDraws, Is.EqualTo(2)); Assert.That(r.JackpotWins, Is.EqualTo(2));
            Assert.That(r.WeeklyWins.Values, Is.EqualTo(new long[] { 1, 1 })); Assert.That(bits.Calls, Is.EqualTo(28));
        }
        [Test]
        public void ConfiguredWeeklyAllowanceGreaterThanOneIsRespected()
        {
            var r = SlotSimulationRunner.Run(WithRules(cap: 2, probability: 1),
                new SlotSimulationOptions(6, jackpotMode: JackpotSimulationMode.WeeklyCapped, weeks: 2, paidSpinsPerWeek: 3),
                new ScriptedReels(ScatterStops(false)));
            Assert.That(r.JackpotDraws, Is.EqualTo(4)); Assert.That(r.JackpotWins, Is.EqualTo(4));
            var e = SlotSimulationReport.JackpotExpectation(r);
            Assert.That(e.meanWins, Is.EqualTo(4)); Assert.That(e.varianceWins, Is.Zero);
        }
        [TestCase(JackpotSimulationMode.Disabled)]
        [TestCase(JackpotSimulationMode.Uncapped)]
        [TestCase(JackpotSimulationMode.WeeklyCapped)]
        public void DisabledJackpotHasNoWinsOrRandomCalls(JackpotSimulationMode mode)
        {
            var bits = new Bits();
            var r = SlotSimulationRunner.Run(WithRules(enabled: false),
                new SlotSimulationOptions(2, jackpotMode: mode, weeks: 1, paidSpinsPerWeek: 2), new ScriptedReels(ScatterStops(false)), bits);
            Assert.That(r.JackpotDraws, Is.Zero); Assert.That(r.JackpotPayout, Is.Zero); Assert.That(bits.Calls, Is.Zero);
        }
        [Test]
        public void EventBoundariesAreInclusiveStartExclusiveEndAndWeekResetIsConfigured()
        {
            var start = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
            var r = SlotSimulationRunner.Run(WithRules(probability: 1, start: start, end: start.AddDays(7)),
                new SlotSimulationOptions(3, jackpotMode: JackpotSimulationMode.WeeklyCapped, weeks: 3, paidSpinsPerWeek: 1),
                new ScriptedReels(ScatterStops(false)));
            Assert.That(r.WeeklyWins.Values, Is.EqualTo(new long[] { 0, 1, 0 }));
            Assert.That(SlotSimulationReport.JackpotExpectation(r).meanWins, Is.EqualTo(1));
        }
        [Test]
        public void IndependentJackpotStreamDoesNotChangeReelStatistics()
        {
            var off = SlotSimulationRunner.Run(config, new SlotSimulationOptions(1000));
            var on = SlotSimulationRunner.Run(config, new SlotSimulationOptions(1000, jackpotMode: JackpotSimulationMode.Uncapped));
            Assert.That(on.BasePayout, Is.EqualTo(off.BasePayout)); Assert.That(on.FreePayout, Is.EqualTo(off.FreePayout));
            Assert.That(on.ScatterCounts, Is.EqualTo(off.ScatterCounts)); Assert.That(on.PaidPayoutDistribution, Is.EqualTo(off.PaidPayoutDistribution));
            Assert.That(SlotSimulationReport.JackpotExpectation(on).meanWins * config.Jackpot.Amount / on.Wager, Is.EqualTo(.01).Within(1e-12));
        }
        [TestCase(200)] [TestCase(600)] [TestCase(1000)]
        public void JackpotExpectationUsesEffectivePaidBet(long bet)
        {
            var r = SlotSimulationRunner.Run(config, new SlotSimulationOptions(10, bet, jackpotMode: JackpotSimulationMode.Uncapped));
            Assert.That(SlotSimulationReport.JackpotExpectation(r).meanWins, Is.EqualTo(10 * .0001 * bet / 1000).Within(1e-12));
        }
        [Test]
        public void RunningVarianceMatchesKnownDatasetIncludingZeros()
        {
            var moments = new RunningMoments();
            Assert.That(moments.StandardError, Is.Zero);
            foreach (var v in new double[] { 0, 2, 4, 6 }) moments.Add(v);
            Assert.That(moments.Mean, Is.EqualTo(3)); Assert.That(moments.Variance, Is.EqualTo(20.0 / 3).Within(1e-12));
            Assert.That(moments.StandardError, Is.EqualTo(Math.Sqrt(5.0 / 3)).Within(1e-12));
            Assert.Throws<ArgumentOutOfRangeException>(() => moments.Add(double.NaN));
        }
        [Test]
        public void WilsonBoundsRemainValidForRareEventsAndZeroWins()
        {
            var interval = SlotSimulationReport.Wilson(0, 1000);
            Assert.That(interval.lower, Is.Zero.Within(1e-12)); Assert.That(interval.upper, Is.GreaterThan(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SlotSimulationReport.Wilson(0, 0));
        }
        [TestCase(0)] [TestCase(-1)]
        public void InvalidPaidSpinCountsRejected(long count)
        { Assert.Throws<ArgumentOutOfRangeException>(() => new SlotSimulationOptions(count)); }
        [Test]
        public void InvalidBetScheduleGuardAndOverflowRejectedClearly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SlotSimulationRunner.Run(config, new SlotSimulationOptions(1, 99)));
            Assert.Throws<ArgumentException>(() => new SlotSimulationOptions(10, jackpotMode: JackpotSimulationMode.WeeklyCapped));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlotSimulationOptions(1, maxBonusChain: 0));
            Assert.Throws<OverflowException>(() => new SlotSimulationOptions(long.MaxValue));
            var rng = new Bits();
            Assert.Throws<ArgumentException>(() => SlotSimulationRunner.Run(config, new SlotSimulationOptions(1), rng, rng));
        }
        [Test]
        public void NonConvergentRetriggerModelFailsBeforeRunning()
        {
            var rules = new SlotRuntimeConfig(config.ConfigVersion, 5, 3, config.ScatterId, config.Symbols,
                config.ReelStrips, config.Paylines, config.Paytable, config.Economy,
                new ScatterRules(new[] { new ScatterAward(3, 1000), new ScatterAward(4, 1000), new ScatterAward(5, 1000) }, 1, true),
                config.DailyBonus, config.Jackpot, config.DebugRandom);
            Assert.Throws<InvalidOperationException>(() => SlotSimulationRunner.Run(rules, new SlotSimulationOptions(1)));
        }
        [Test]
        public void AccumulatedCoinPayoutOverflowFailsRatherThanWrapping()
        {
            var rules = new SlotRuntimeConfig(config.ConfigVersion, 5, 3, config.ScatterId, config.Symbols,
                config.ReelStrips, config.Paylines,
                new Paytable(config.Paytable.Entries.Select(p => new SymbolPayout(p.Symbol, long.MaxValue / 1100,
                    long.MaxValue / 1100, long.MaxValue / 1100))), config.Economy, config.Scatter,
                config.DailyBonus, config.Jackpot, config.DebugRandom);
            var stops = config.ReelStrips.Select(s => Enumerable.Range(0, s.Symbols.Count).First(i =>
                s.Symbols[(i + 1) % s.Symbols.Count] == SymbolId.S1 &&
                !Enumerable.Range(0, 3).Any(row => s.Symbols[(i + row) % s.Symbols.Count] == config.ScatterId))).ToArray();
            Assert.Throws<OverflowException>(() => SlotSimulationRunner.Run(rules, new SlotSimulationOptions(100), new ScriptedReels(stops)));
        }

        [Test, Explicit("Large validation and report generation; select this test by its full name."), Category("LargeSimulation")]
        public void GenerateSection412Report()
        {
            var report = new StringBuilder("# Section 4.12 — executed mathematical validation\n\n");
            report.AppendLine("Environment: Unity 6000.3.10f1 Windows Editor, actual production SlotEngine and serialized SlotGameConfigSO. Test-only SplitMix64 seed 412; separate jackpot stream seed = seed XOR D1B54A32D192ED03. Production RNG/settings unchanged. System.Random only guarantees sequences on the same runtime; this harness does not rely on it. Counts and monetary totals use checked Int64; samples use Welford moments. No scene, UI, player save, file I/O per spin, or wall-clock eligibility. Aggregate memory does not retain grids; histograms are bounded by configured payouts and the bonus guard.\n");
            bool allPassed = true;
            foreach (var options in new[] {
                new SlotSimulationOptions(10000), new SlotSimulationOptions(100000), new SlotSimulationOptions(1000000),
                new SlotSimulationOptions(1000000, jackpotMode: JackpotSimulationMode.Uncapped),
                new SlotSimulationOptions(1000000, jackpotMode: JackpotSimulationMode.WeeklyCapped, weeks: 100, paidSpinsPerWeek: 10000) })
            {
                var r = SlotSimulationRunner.Run(config, options);
                report.AppendLine(SlotSimulationReport.Markdown(r, out var passed)); allPassed &= passed;
                TestContext.WriteLine($"{r.PaidSpins} {options.JackpotMode}: base={r.BasePayout}, free={r.FreePayout}, FS={r.FreeSpins}, slotRtp={r.SlotRtp:R}, SE={r.SlotReturns.StandardError:R}, jackpots={r.JackpotWins}, seconds={r.Seconds:R}, passed={passed}");
            }
            report.AppendLine("## Scope and remaining QA\n\nNo production mathematical defect found; no correction/rebalancing performed. Existing EditMode lifecycle fixtures cover double Spin, quit after debit, quit after settlement, failed save retry, weekly acceptance boundary and daily reset boundary. This is simulated application recovery, not section 4.13 process-kill/UI QA.\n\nDeferred to section 4.13: Back partly outside 4:3 viewport; six-digit Balance and LastWin can touch. SC.unity, anchors, presentation and production config remain untouched. No section 4.13 work performed.\n\nReproduce: select `IceP.Tests.SlotSimulationTests.GenerateSection412Report` explicitly in Unity Test Runner. Regular EditMode runs skip this expensive test; smoke is 10,000 paid spins. Public API: `SlotSimulationRunner.Run(config, new SlotSimulationOptions(paidSpins, bet, seed, maxBonusChain, jackpotMode, weeks, paidSpinsPerWeek, startUtc))`; `SlotAnalyticalModel.Calculate(config)`; `SlotSimulationReport.Markdown(result, out passed)`. Optional injected `IRandomSource` instances support scripted regressions.\n");
            File.WriteAllText("Assets/Docs/Section412SimulationReport.md", report.ToString(), new UTF8Encoding(false));
            Assert.That(allPassed, Is.True, "See generated report; statistically unexpected deviations require investigation.");
        }
    }
}
