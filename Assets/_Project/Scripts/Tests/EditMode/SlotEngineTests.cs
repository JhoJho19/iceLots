using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IceP.Tests
{
    public sealed class SlotEngineTests
    {
        private SlotRuntimeConfig config;

        [SetUp]
        public void SetUp()
        {
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>(
                "Assets/_Project/Configs/SlotGameConfigSO.asset"));
            try { config = new LocalSlotConfigProvider(source).Load(); }
            finally { Object.DestroyImmediate(source); }
        }

        [TestCase(3, 2850)]
        [TestCase(4, 16500)]
        [TestCase(5, 85000)]
        public void S7PaysOnlyLongestLeftPrefixOnOneLine(int count, long expected)
        {
            var grid = Prefix(SymbolId.S7, count);
            var result = new WinEvaluator(config).Evaluate(grid, 1000);
            Assert.That(result.BaseWin, Is.EqualTo(expected));
            Assert.That(result.WinningLines.Count, Is.EqualTo(1));
            var line = result.WinningLines.Single();
            Assert.That(line.PaylineIndex, Is.EqualTo(0));
            Assert.That(line.Symbol, Is.EqualTo(SymbolId.S7));
            Assert.That(line.MatchCount, Is.EqualTo(count));
            Assert.That(line.PayoutMultiplier, Is.EqualTo(expected / 50));
            Assert.That(line.Coordinates.Select(c => (c.ReelIndex, c.RowIndex)),
                Is.EqualTo(Enumerable.Range(0, count).Select(i => (i, 1))));
        }

        [TestCase(SymbolId.S1, 13, 57, 210)]
        [TestCase(SymbolId.S2, 16, 75, 290)]
        [TestCase(SymbolId.S3, 20, 90, 375)]
        [TestCase(SymbolId.S4, 24, 115, 500)]
        [TestCase(SymbolId.S5, 27, 135, 580)]
        [TestCase(SymbolId.S6, 37, 195, 850)]
        [TestCase(SymbolId.S7, 57, 330, 1700)]
        public void EachOrdinarySymbolUsesItsConfiguredPayouts(SymbolId symbol, long three, long four, long five)
        {
            // Scatter background isolates the center line for every ordinary symbol.
            foreach (var pair in new[] { (3, three), (4, four), (5, five) })
            {
                var cells = Filled(SymbolId.SCATTER);
                for (var reel = 0; reel < pair.Item1; reel++) cells[reel, 1] = symbol;
                var result = new WinEvaluator(config).Evaluate(new SlotGrid(cells), 1000);
                Assert.That(result.WinningLines.Count, Is.EqualTo(1));
                Assert.That(result.BaseWin, Is.EqualTo(50 * pair.Item2));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ShortPrefixesAndLosingGridPayNothing(int count)
        {
            var result = new WinEvaluator(config).Evaluate(Prefix(SymbolId.S7, count), 1000);
            Assert.That(result.BaseWin, Is.Zero);
            Assert.That(result.WinningLines, Is.Empty);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void MatchingRunStartingAfterFirstReelDoesNotPay(int firstReel)
        {
            var cells = LosingCells();
            for (var reel = firstReel; reel < 5; reel++) cells[reel, 1] = SymbolId.S7;
            Assert.That(new WinEvaluator(config).Evaluate(new SlotGrid(cells), 1000).BaseWin, Is.Zero);
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(2, 0)]
        [TestCase(3, 2850)]
        [TestCase(4, 16500)]
        public void ScatterInterruptsPrefix(int scatterReel, long expected)
        {
            var cells = LosingCells();
            for (var reel = 0; reel < 5; reel++) cells[reel, 1] = SymbolId.S7;
            cells[scatterReel, 1] = SymbolId.SCATTER;
            Assert.That(new WinEvaluator(config).Evaluate(new SlotGrid(cells), 1000).BaseWin, Is.EqualTo(expected));
        }

        [Test]
        public void ScatterNeverPaysOnAnyLine()
        {
            var result = new WinEvaluator(config).Evaluate(new SlotGrid(Filled(SymbolId.SCATTER)), 1000);
            Assert.That(result.BaseWin, Is.Zero);
            Assert.That(result.WinningLines, Is.Empty);
        }

        [Test]
        public void AllTwentyWinningLinesAreIndependentAndSummed()
        {
            var result = new WinEvaluator(config).Evaluate(new SlotGrid(Filled(SymbolId.S7)), 1000);
            Assert.That(result.WinningLines.Select(l => l.PaylineIndex), Is.EqualTo(Enumerable.Range(0, 20)));
            Assert.That(result.WinningLines.All(l => l.MatchCount == 5 && l.Payout == 85000), Is.True);
            Assert.That(result.BaseWin, Is.EqualTo(1700000));
            foreach (var line in result.WinningLines)
                Assert.That(line.Coordinates.Select(c => c.RowIndex), Is.EqualTo(config.Paylines[line.PaylineIndex].Rows));
        }

        [Test]
        public void DifferentlyPayingLinesAreSummed()
        {
            var cells = Filled(SymbolId.SCATTER);
            for (var reel = 0; reel < 5; reel++)
            {
                cells[reel, 0] = SymbolId.S1;
                cells[reel, 1] = SymbolId.S7;
                cells[reel, 2] = SymbolId.S3;
            }
            var result = new WinEvaluator(config).Evaluate(new SlotGrid(cells), 1000);
            Assert.That(result.WinningLines.Select(l => l.PaylineIndex), Is.EqualTo(new[] { 0, 1, 2, 19 }));
            Assert.That(result.BaseWin, Is.EqualTo(85000 + 10500 + 18750 + 650));
        }

        [TestCase(200, 17000)]
        [TestCase(400, 34000)]
        [TestCase(600, 51000)]
        [TestCase(800, 68000)]
        [TestCase(1000, 85000)]
        public void TotalBetIsDividedAmongAllActiveLines(long bet, long payout)
        {
            Assert.That(new WinEvaluator(config).Evaluate(Prefix(SymbolId.S7, 5), bet).BaseWin, Is.EqualTo(payout));
        }

        [Test]
        public void ChangingPaytableChangesWinAndZeroLongestRewardDoesNotPayShorterPrefixes()
        {
            var entries = config.Paytable.Entries.Select(p => p.Symbol == SymbolId.S7
                ? new SymbolPayout(p.Symbol, 7, 11, 19) : p).ToArray();
            var changed = With(paytable: new Paytable(entries));
            Assert.That(new WinEvaluator(changed).Evaluate(Prefix(SymbolId.S7, 5), 1000).BaseWin, Is.EqualTo(950));
            entries[6] = new SymbolPayout(SymbolId.S7, 57, 330, 0);
            Assert.That(new WinEvaluator(With(paytable: new Paytable(entries)))
                .Evaluate(Prefix(SymbolId.S7, 5), 1000).BaseWin, Is.Zero);
            Assert.That(new WinEvaluator(config).Evaluate(Prefix(SymbolId.S7, 5), 1000).BaseWin, Is.EqualTo(85000));
        }

        [TestCase(-20)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(201)]
        [TestCase(20)]
        [TestCase(1200)]
        public void InvalidBetsAreRejectedBeforeRng(long bet)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WinEvaluator(config).Evaluate(Prefix(SymbolId.S7, 5), bet));
            var rng = new StopsRandom();
            var engine = new IceP.Domain.SlotEngine.SlotEngine(config, rng);
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Generate(SpinEvaluationContext.Paid(bet)));
            Assert.That(rng.Calls, Is.Zero);
        }

        [Test]
        public void DefaultContextAndNullArgumentsAreRejected()
        {
            var engine = new IceP.Domain.SlotEngine.SlotEngine(config, new StopsRandom());
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Generate(default));
            Assert.Throws<ArgumentNullException>(() => engine.Evaluate(null, SpinEvaluationContext.Paid(1000)));
            Assert.Throws<ArgumentNullException>(() => new WinEvaluator(config).Evaluate(null, 1000));
            Assert.Throws<ArgumentNullException>(() => new ScatterEvaluator(config).Evaluate(null));
            Assert.Throws<ArgumentNullException>(() => new WinEvaluator(null));
            Assert.Throws<ArgumentNullException>(() => new ScatterEvaluator(null));
            Assert.Throws<ArgumentNullException>(() => new JackpotRules(null));
        }

        [TestCase("line")]
        [TestCase("sum")]
        [TestCase("free")]
        public void UnsafeMonetaryConfigurationsAreRejectedBeforeEvaluation(string boundary)
        {
            var multiplier = boundary == "line" ? long.MaxValue : long.MaxValue / 500;
            var entries = config.Paytable.Entries.Select(p => new SymbolPayout(p.Symbol, 0, 0, multiplier));
            var changed = With(paytable: new Paytable(entries), scatter: new ScatterRules(config.Scatter.Awards,
                boundary == "free" ? 2 : 1, true));
            // line: 50 * MaxValue; sum: each line fits, twenty-line sum overflows;
            // free: base sum fits, applying the Free Spins multiplier overflows.
            if (boundary == "free")
                changed = With(paytable: new Paytable(config.Paytable.Entries.Select(p =>
                    new SymbolPayout(p.Symbol, 0, 0, long.MaxValue / 1500))),
                    scatter: new ScatterRules(config.Scatter.Awards, 2, true));
            Assert.That(ConfigValidator.Validate(changed).Any(e => e.StartsWith("PayoutBounds:")), Is.True);
            Assert.Throws<ConfigValidationException>(() => new WinEvaluator(changed));
            Assert.Throws<ConfigValidationException>(() => new IceP.Domain.SlotEngine.SlotEngine(changed, new StopsRandom()));
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(2, 0)]
        [TestCase(3, 8)]
        [TestCase(4, 12)]
        [TestCase(5, 20)]
        public void ScatterCountsAllCellsAndReturnsExactRewards(int count, int spins)
        {
            var grid = ScatterGrid(count);
            foreach (var free in new[] { false, true })
            {
                var result = new ScatterEvaluator(config).Evaluate(grid, free);
                Assert.That(result.ScatterCount, Is.EqualTo(count));
                Assert.That(result.AwardedFreeSpins, Is.EqualTo(spins));
                Assert.That(result.BonusTriggered, Is.EqualTo(spins > 0));
            }
            Assert.That(new WinEvaluator(config).Evaluate(grid, 1000).BaseWin, Is.Zero);
        }

        [Test]
        public void ScatterPositionAndOrdinarySymbolIdentityDoNotAffectRewards()
        {
            foreach (var ordinary in new[] { SymbolId.S1, SymbolId.S7 })
            {
                var cells = Filled(ordinary);
                cells[0, 0] = cells[1, 2] = cells[4, 0] = SymbolId.SCATTER;
                Assert.That(new ScatterEvaluator(config).Evaluate(new SlotGrid(cells)).AwardedFreeSpins, Is.EqualTo(8));
            }
        }

        [Test]
        public void ScatterRewardsAreConfiguredAndRetriggerSwitchOnlyAffectsFreeSpins()
        {
            var changed = With(scatter: new ScatterRules(new[] {
                new ScatterAward(3, 9), new ScatterAward(4, 15), new ScatterAward(5, 25) }, 1, false));
            var evaluator = new ScatterEvaluator(changed);
            foreach (var pair in new[] { (3, 9), (4, 15), (5, 25) })
            {
                Assert.That(evaluator.Evaluate(ScatterGrid(pair.Item1)).AwardedFreeSpins, Is.EqualTo(pair.Item2));
                var free = evaluator.Evaluate(ScatterGrid(pair.Item1), true);
                Assert.That(free.ScatterCount, Is.EqualTo(pair.Item1));
                Assert.That(free.AwardedFreeSpins, Is.Zero);
                Assert.That(free.BonusTriggered, Is.False);
            }
            var zero = With(scatter: new ScatterRules(config.Scatter.Awards.Select(a => new ScatterAward(a.ScatterCount, 0)), 1, true));
            Assert.That(new ScatterEvaluator(zero).Evaluate(ScatterGrid(5)).BonusTriggered, Is.False);
        }

        [Test]
        public void CountsAboveFiveAreCountedButHaveNoConfiguredReward()
        {
            var result = new ScatterEvaluator(config).Evaluate(new SlotGrid(Filled(SymbolId.SCATTER)));
            Assert.That(result.ScatterCount, Is.EqualTo(15));
            Assert.That(result.AwardedFreeSpins, Is.Zero);
        }

        [Test]
        public void PredefinedOutcomeUsesBonusBetAndConfiguredMultiplierWithoutRngOrMutation()
        {
            var cells = LosingCells();
            for (var reel = 0; reel < 5; reel++) cells[reel, 1] = SymbolId.S7;
            cells[0, 0] = cells[1, 2] = cells[4, 0] = SymbolId.SCATTER;
            var outcome = new SpinOutcome(new SlotGrid(cells), Enumerable.Range(0, 5).Select(i => new ReelStop(i, i)));
            var beforeReels = config.ReelStrips.Select(r => r.Symbols.ToArray()).ToArray();
            var rng = new StopsRandom();
            var engine = new IceP.Domain.SlotEngine.SlotEngine(With(scatter: new ScatterRules(config.Scatter.Awards, 3, true)), rng);
            var paid = engine.Evaluate(outcome, SpinEvaluationContext.Paid(1000));
            var free = engine.Evaluate(outcome, SpinEvaluationContext.Free(1000));
            var repeat = engine.Evaluate(outcome, SpinEvaluationContext.Free(1000));
            Assert.That(paid.Wins.BaseWin, Is.EqualTo(85000));
            Assert.That(paid.FinalPayout, Is.EqualTo(85000));
            Assert.That(free.Wins.BaseWin, Is.EqualTo(85000));
            Assert.That(free.FinalPayout, Is.EqualTo(255000));
            Assert.That(free.Context.EvaluationBet, Is.EqualTo(1000));
            Assert.That(free.AppliedPayoutMultiplier, Is.EqualTo(3));
            Assert.That(free.Scatter.ScatterCount, Is.EqualTo(3));
            Assert.That(free.Scatter.AwardedFreeSpins, Is.EqualTo(8));
            Assert.That(free.Outcome, Is.SameAs(outcome));
            Assert.That(repeat.FinalPayout, Is.EqualTo(free.FinalPayout));
            Assert.That(repeat.Scatter.AwardedFreeSpins, Is.EqualTo(free.Scatter.AwardedFreeSpins));
            Assert.That(repeat.Wins.WinningLines.Select(l => (l.PaylineIndex, l.Symbol, l.MatchCount, l.Payout)),
                Is.EqualTo(free.Wins.WinningLines.Select(l => (l.PaylineIndex, l.Symbol, l.MatchCount, l.Payout))));
            Assert.That(rng.Calls, Is.Zero);
            for (var reel = 0; reel < 5; reel++)
            {
                Assert.That(outcome.Stops[reel].Position, Is.EqualTo(reel));
                Assert.That(config.ReelStrips[reel].Symbols, Is.EqualTo(beforeReels[reel]));
                for (var row = 0; row < 3; row++) Assert.That(outcome.Grid[reel, row], Is.EqualTo(cells[reel, row]));
            }
            Assert.Throws<NotSupportedException>(() => ((IList<LineWin>)free.Wins.WinningLines).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<GridCoordinate>)free.Wins.WinningLines[0].Coordinates).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<ReelStop>)outcome.Stops).Clear());
            Assert.That(new IceP.Domain.SlotEngine.SlotEngine(config, rng)
                .Evaluate(outcome, SpinEvaluationContext.Free(1000)).FinalPayout, Is.EqualTo(85000));
        }

        [Test]
        public void EngineGeneratesFiveStopsAndEvaluatesTheSameResultAsStandaloneComponents()
        {
            var rng = new StopsRandom(0, 28, 29, 27, 5);
            var result = new IceP.Domain.SlotEngine.SlotEngine(config, rng).Generate(SpinEvaluationContext.Paid(1000));
            Assert.That(rng.Calls, Is.EqualTo(5));
            Assert.That(result.Outcome.Stops.Select(s => s.Position), Is.EqualTo(new[] { 0, 28, 29, 27, 5 }));
            var expected = new OutcomeGenerator(config, new StopsRandom(0, 28, 29, 27, 5)).Generate();
            for (var reel = 0; reel < 5; reel++)
                for (var row = 0; row < 3; row++) Assert.That(result.Outcome.Grid[reel, row], Is.EqualTo(expected.Grid[reel, row]));
            Assert.That(result.Wins.BaseWin, Is.EqualTo(new WinEvaluator(config).Evaluate(expected.Grid, 1000).BaseWin));
            Assert.That(result.Scatter.ScatterCount, Is.EqualTo(new ScatterEvaluator(config).Evaluate(expected.Grid).ScatterCount));
            Assert.That(result.FinalPayout, Is.EqualTo(result.Wins.BaseWin));
        }

        [Test]
        public void FreeSpinGenerationUsesTheSameReelsAndRejectsUnconfiguredBonusBet()
        {
            var paid = new IceP.Domain.SlotEngine.SlotEngine(config, new StopsRandom(29, 28, 27, 26, 25))
                .Generate(SpinEvaluationContext.Paid(400));
            var free = new IceP.Domain.SlotEngine.SlotEngine(config, new StopsRandom(29, 28, 27, 26, 25))
                .Generate(SpinEvaluationContext.Free(400));
            Assert.That(free.Outcome.Stops.Select(s => s.Position), Is.EqualTo(paid.Outcome.Stops.Select(s => s.Position)));
            for (var reel = 0; reel < 5; reel++)
                for (var row = 0; row < 3; row++) Assert.That(free.Outcome.Grid[reel, row], Is.EqualTo(paid.Outcome.Grid[reel, row]));
            Assert.That(free.Wins.BaseWin, Is.EqualTo(paid.Wins.BaseWin));
            var rng = new StopsRandom();
            Assert.Throws<ArgumentOutOfRangeException>(() => new IceP.Domain.SlotEngine.SlotEngine(config, rng)
                .Generate(SpinEvaluationContext.Free(1200)));
            Assert.That(rng.Calls, Is.Zero);
        }

        [TestCase(200, 0.00002)]
        [TestCase(400, 0.00004)]
        [TestCase(600, 0.00006)]
        [TestCase(800, 0.00008)]
        [TestCase(1000, 0.00010)]
        public void JackpotProbabilityForSupportedBets(long bet, double expected)
        {
            var rules = new JackpotRules(config.Jackpot);
            Assert.That(rules.CalculateProbability(SpinEvaluationContext.Paid(bet)), Is.EqualTo(expected).Within(1e-15));
            Assert.That(rules.CalculateProbability(SpinEvaluationContext.Free(bet)), Is.Zero);
        }

        [Test]
        public void JackpotHonorsDisabledAndWeeklyConditions()
        {
            var paid = SpinEvaluationContext.Paid(1000);
            Assert.That(new JackpotRules(Jackpot(enabled: false)).CalculateProbability(paid), Is.Zero);
            var rules = new JackpotRules(config.Jackpot);
            Assert.That(rules.CalculateProbability(paid, weeklyEligible: false), Is.Zero);
            Assert.That(rules.CalculateProbability(paid, alreadyWonThisWeek: true), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.CalculateProbability(default));
        }

        [TestCase(0, 1000, 0)]
        [TestCase(1, 1000, 1)]
        [TestCase(1, 2000, 1)]
        [TestCase(0.5, 1000, 0.5)]
        public void JackpotZeroOneAndClamping(double probability, long bet, double expected)
        {
            Assert.That(new JackpotRules(Jackpot(probability: probability))
                .CalculateProbability(SpinEvaluationContext.Paid(bet)), Is.EqualTo(expected));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void JackpotRejectsInvalidReferenceBet(long reference)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JackpotRules(Jackpot(reference: reference)));
        }

        [TestCase(-0.1)]
        [TestCase(1.1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void JackpotRejectsInvalidProbability(double probability)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JackpotRules(Jackpot(probability: probability)));
        }

        [Test]
        public void JackpotRespectsExplicitEventTimeWithInclusiveStartExclusiveEnd()
        {
            var start = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
            var end = start.AddDays(7);
            var rules = new JackpotRules(Jackpot(start: start, end: end));
            var paid = SpinEvaluationContext.Paid(1000);
            Assert.That(rules.CalculateProbability(paid, acceptedAtUtc: start.AddTicks(-1)), Is.Zero);
            Assert.That(rules.CalculateProbability(paid, acceptedAtUtc: start), Is.GreaterThan(0));
            Assert.That(rules.CalculateProbability(paid, acceptedAtUtc: end.AddTicks(-1)), Is.GreaterThan(0));
            Assert.That(rules.CalculateProbability(paid, acceptedAtUtc: end), Is.Zero);
        }

        [Test]
        public void DomainAndAllEvaluationDtosHaveNoForbiddenDependenciesOrMutableProperties()
        {
            var assembly = typeof(WinEvaluator).Assembly;
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.That(references.Any(n => n.StartsWith("Unity") || n.StartsWith("IceP.Application") ||
                n.StartsWith("IceP.Infrastructure") || n.StartsWith("IceP.Presentation")), Is.False);
            foreach (var type in new[] { typeof(LineWin), typeof(GridCoordinate), typeof(WinEvaluation),
                typeof(ScatterEvaluation), typeof(SpinEvaluationContext), typeof(EvaluatedSpinOutcome) })
                Assert.That(type.GetProperties().Any(p => p.GetSetMethod(true) != null), Is.False, type.Name);
            Assert.That(typeof(IceP.Domain.SlotEngine.SlotEngine).BaseType, Is.EqualTo(typeof(object)));
            foreach (var type in new[] { typeof(WinEvaluator), typeof(ScatterEvaluator), typeof(JackpotRules), typeof(IceP.Domain.SlotEngine.SlotEngine) })
                Assert.That(type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), Is.Empty);
        }

        private SlotRuntimeConfig With(Paytable paytable = null, ScatterRules scatter = null) =>
            new SlotRuntimeConfig(config.ConfigVersion, config.ReelCount, config.VisibleRows, config.ScatterId,
                config.Symbols, config.ReelStrips, config.Paylines, paytable ?? config.Paytable, config.Economy,
                scatter ?? config.Scatter, config.DailyBonus, config.Jackpot, config.DebugRandom);

        private JackpotRulesConfig Jackpot(bool enabled = true, double probability = 0.0001, long reference = 1000,
            DateTimeOffset? start = null, DateTimeOffset? end = null) =>
            new JackpotRulesConfig(enabled, "weekly-jackpot", "1.0.0", 100000, probability, reference, 1,
                DayOfWeek.Monday, TimeSpan.Zero, start, end);

        private static SymbolId[,] Filled(SymbolId symbol)
        {
            var cells = new SymbolId[5, 3];
            for (var reel = 0; reel < 5; reel++) for (var row = 0; row < 3; row++) cells[reel, row] = symbol;
            return cells;
        }
        private static SymbolId[,] LosingCells()
        {
            var cells = new SymbolId[5, 3];
            for (var reel = 0; reel < 5; reel++) for (var row = 0; row < 3; row++) cells[reel, row] = reel % 2 == 0 ? SymbolId.S1 : SymbolId.S2;
            return cells;
        }
        private static SlotGrid Prefix(SymbolId symbol, int count)
        {
            var cells = LosingCells();
            for (var reel = 0; reel < count; reel++) cells[reel, 1] = symbol;
            return new SlotGrid(cells);
        }
        private static SlotGrid ScatterGrid(int count)
        {
            var cells = LosingCells();
            // Rows alternate across reels; these positions do not form any configured payline.
            for (var reel = 0; reel < count; reel++) cells[reel, reel % 2 == 0 ? 0 : 2] = SymbolId.SCATTER;
            return new SlotGrid(cells);
        }
        private sealed class StopsRandom : IRandomSource
        {
            private readonly int[] stops;
            public int Calls { get; private set; }
            public StopsRandom(params int[] stops) { this.stops = stops; }
            public int NextInt(int minInclusive, int maxExclusive)
            {
                var value = stops.Length == 0 ? 0 : stops[Calls % stops.Length];
                Calls++;
                Assert.That((minInclusive, maxExclusive), Is.EqualTo((0, 30)));
                return value;
            }
        }
    }
}
