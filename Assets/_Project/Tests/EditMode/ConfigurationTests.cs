using System;
using System.Collections.Generic;
using System.Linq;
using IceP.Application;
using IceP.Domain.Configuration;
using IceP.Infrastructure.Configuration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IceP.Tests
{
    public sealed class ConfigurationTests
    {
        private SlotGameConfigSO game;
        private SlotVisualConfigSO visual;

        [SetUp]
        public void SetUp()
        {
            var gameAsset = AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset");
            var visualAsset = AssetDatabase.LoadAssetAtPath<SlotVisualConfigSO>("Assets/_Project/Configs/SlotVisualConfigSO.asset");
            Assert.That(gameAsset, Is.Not.Null, "The serialized default game asset must exist.");
            Assert.That(visualAsset, Is.Not.Null, "The serialized default visual asset must exist.");
            game = Object.Instantiate(gameAsset);
            visual = Object.Instantiate(visualAsset);
        }

        [TearDown]
        public void TearDown()
        {
            if (game) Object.DestroyImmediate(game);
            if (visual) Object.DestroyImmediate(visual);
        }

        [Test]
        public void DefaultAssetsMatchSpecificationAndPassValidation()
        {
            ISlotConfigProvider provider = new LocalSlotConfigProvider(game);
            var config = provider.Load();
            Assert.That(ConfigValidator.Validate(config), Is.Empty);
            Assert.That(VisualConfigValidator.Validate(visual, config), Is.Empty);
            Assert.That(config.ConfigVersion, Is.EqualTo("1.0.0"));
            Assert.That(config.ReelCount, Is.EqualTo(5));
            Assert.That(config.VisibleRows, Is.EqualTo(3));
            Assert.That(config.ScatterId, Is.EqualTo(SymbolId.SCATTER));
            Assert.That(config.Symbols.Select(s => s.Id), Is.EqualTo(Enum.GetValues(typeof(SymbolId))));
            var frequencies = new[] { 6, 5, 5, 4, 4, 3, 2, 1 };
            Assert.That(config.Symbols.Select(s => s.RequiredCountPerReel), Is.EqualTo(frequencies));
            Assert.That(config.ReelStrips.Count, Is.EqualTo(5));
            foreach (var strip in config.ReelStrips)
            {
                Assert.That(strip.Symbols.Count, Is.EqualTo(30));
                for (var i = 0; i < frequencies.Length; i++)
                    Assert.That(strip.Symbols.Count(s => s == (SymbolId)i), Is.EqualTo(frequencies[i]));
            }
            Assert.That(config.ReelStrips.Select(r => string.Join(",", r.Symbols)).Distinct().Count(), Is.EqualTo(5));
            var expectedLines = new[] { "1,1,1,1,1", "0,0,0,0,0", "2,2,2,2,2", "0,1,2,1,0", "2,1,0,1,2",
                "0,0,1,0,0", "2,2,1,2,2", "1,0,0,0,1", "1,2,2,2,1", "1,0,1,0,1", "1,2,1,2,1", "0,1,0,1,0",
                "2,1,2,1,2", "0,1,1,1,0", "2,1,1,1,2", "1,1,0,1,1", "1,1,2,1,1", "0,0,1,2,2", "2,2,1,0,0", "0,0,0,1,2" };
            Assert.That(config.Paylines.Select(p => string.Join(",", p.Rows)), Is.EqualTo(expectedLines));
            Assert.That(config.Paytable.Entries.Select(e => e.Symbol), Is.EqualTo(Enumerable.Range(0, 7).Select(i => (SymbolId)i)));
            Assert.That(config.Paytable.Entries.Select(e => e.Match3), Is.EqualTo(new long[] { 13, 16, 20, 24, 27, 37, 57 }));
            Assert.That(config.Paytable.Entries.Select(e => e.Match4), Is.EqualTo(new long[] { 57, 75, 90, 115, 135, 195, 330 }));
            Assert.That(config.Paytable.Entries.Select(e => e.Match5), Is.EqualTo(new long[] { 210, 290, 375, 500, 580, 850, 1700 }));
            Assert.That(config.Economy.StartingBalance, Is.EqualTo(20000));
            Assert.That(config.Economy.DefaultBet, Is.EqualTo(200));
            Assert.That(config.Economy.AvailableBets, Is.EqualTo(new long[] { 200, 400, 600, 800, 1000 }));
            Assert.That(config.Scatter.Awards.Select(a => a.ScatterCount), Is.EqualTo(new[] { 3, 4, 5 }));
            Assert.That(config.Scatter.Awards.Select(a => a.FreeSpins), Is.EqualTo(new[] { 8, 12, 20 }));
            Assert.That(config.Scatter.FreeSpinsMultiplier, Is.EqualTo(1));
            Assert.That(config.Scatter.RetriggerEnabled, Is.True);
            Assert.That(config.DailyBonus.Enabled, Is.True);
            Assert.That(config.DailyBonus.Rewards, Is.EqualTo(new long[] { 400, 600, 800, 1000, 1200, 1600, 3000 }));
            Assert.That(config.DailyBonus.ResetTimeUtc, Is.EqualTo(TimeSpan.Zero));
            Assert.That(config.DailyBonus.StreakResetBehavior, Is.EqualTo(StreakResetBehavior.ResetAfterMissedDay));
            Assert.That(config.Jackpot.Enabled, Is.True);
            Assert.That(config.Jackpot.EventId, Is.EqualTo("weekly-jackpot"));
            Assert.That(config.Jackpot.ConfigVersion, Is.EqualTo("1.0.0"));
            Assert.That(config.Jackpot.Amount, Is.EqualTo(100000));
            Assert.That(config.Jackpot.BaseProbability, Is.EqualTo(0.0001));
            Assert.That(config.Jackpot.ReferenceBet, Is.EqualTo(1000));
            Assert.That(config.Jackpot.MaxWinsPerWeek, Is.EqualTo(1));
            Assert.That(config.Jackpot.ResetWeekday, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(config.Jackpot.ResetTimeUtc, Is.EqualTo(TimeSpan.Zero));
            Assert.That(config.Jackpot.StartUtc, Is.Null);
            Assert.That(config.Jackpot.EndUtc, Is.Null);
            Assert.That(config.DebugRandom.UseFixedSeed, Is.False);
            Assert.That(config.DebugRandom.Seed, Is.EqualTo(game.DebugRandom.Seed));
        }

        [TestCase("reel-count", "ReelStrips")]
        [TestCase("reel-length", "ReelStrips[0].Symbols")]
        [TestCase("reel-symbol", "ReelStrips[0].Symbols[0]")]
        [TestCase("reel-distribution", "ReelStrips[0].Symbols")]
        [TestCase("reel-duplicate", "ReelStrips[1]")]
        [TestCase("reel-null", "ReelStrips[0]")]
        [TestCase("line-count", "Paylines")]
        [TestCase("line-length", "Paylines[0].Rows")]
        [TestCase("line-row", "Paylines[0].Rows[0]")]
        [TestCase("line-negative-row", "Paylines[0].Rows[0]")]
        [TestCase("line-duplicate", "Paylines[1]")]
        [TestCase("line-null", "Paylines[0]")]
        [TestCase("bet-divisibility", "Economy.AvailableBets[0]")]
        [TestCase("bet-zero", "Economy.AvailableBets[0]")]
        [TestCase("bet-negative", "Economy.AvailableBets[0]")]
        [TestCase("bet-duplicate", "Economy.AvailableBets[1]")]
        [TestCase("default-bet", "Economy.DefaultBet")]
        [TestCase("balance-negative", "Economy.StartingBalance")]
        [TestCase("paytable-negative", "Paytable.Entries[0].Match3")]
        [TestCase("paytable-scatter", "Paytable.Entries[0].Symbol")]
        [TestCase("paytable-duplicate", "Paytable.Entries[1].Symbol")]
        [TestCase("paytable-null", "Paytable[0]")]
        [TestCase("grid-reels", "Grid.ReelCount")]
        [TestCase("grid-rows", "Grid.VisibleRows")]
        [TestCase("scatter-id", "Grid.ScatterId")]
        [TestCase("symbol-unknown", "Symbols[0].Id")]
        [TestCase("symbol-duplicate", "Symbols[1].Id")]
        [TestCase("symbol-count", "Symbols.RequiredCountPerReel")]
        [TestCase("scatter-count", "Symbols.SCATTER.RequiredCountPerReel")]
        [TestCase("scatter-threshold", "Scatter.Awards[0].ScatterCount")]
        [TestCase("scatter-duplicate", "Scatter.Awards[1].ScatterCount")]
        [TestCase("scatter-negative", "Scatter.Awards[0].FreeSpins")]
        [TestCase("free-multiplier", "Scatter.FreeSpinsMultiplier")]
        [TestCase("daily-rewards", "DailyBonus.Rewards")]
        [TestCase("daily-negative", "DailyBonus.Rewards[0]")]
        [TestCase("daily-time", "DailyBonus.ResetTimeUtc")]
        [TestCase("daily-policy", "DailyBonus.StreakResetBehavior")]
        [TestCase("jackpot-reference", "Jackpot.ReferenceBet")]
        [TestCase("jackpot-limit", "Jackpot.MaxWinsPerWeek")]
        [TestCase("jackpot-negative", "Jackpot.Amount")]
        [TestCase("jackpot-weekday", "Jackpot.ResetWeekday")]
        [TestCase("jackpot-time", "Jackpot.ResetTimeUtc")]
        [TestCase("jackpot-version", "Jackpot.ConfigVersion")]
        [TestCase("jackpot-id", "Jackpot.EventId")]
        [TestCase("version", "ConfigVersion")]
        [TestCase("null-grid", "Grid")]
        [TestCase("null-economy", "Economy")]
        [TestCase("null-scatter", "Scatter")]
        [TestCase("null-daily", "DailyBonus")]
        [TestCase("null-jackpot", "Jackpot")]
        [TestCase("null-debug", "DebugRandom")]
        public void InvalidMathematicalFieldsHaveSpecificDiagnostics(string mutation, string field)
        {
            switch (mutation)
            {
                case "reel-count": game.ReelStrips = game.ReelStrips.Take(4).ToArray(); break;
                case "reel-length": game.ReelStrips[0].Symbols = game.ReelStrips[0].Symbols.Take(29).ToArray(); break;
                case "reel-symbol": game.ReelStrips[0].Symbols[0] = (SymbolId)99; break;
                case "reel-distribution": game.ReelStrips[0].Symbols[0] = game.ReelStrips[0].Symbols[0] == SymbolId.S1 ? SymbolId.S2 : SymbolId.S1; break;
                case "reel-duplicate": game.ReelStrips[1].Symbols = game.ReelStrips[0].Symbols.ToArray(); break;
                case "reel-null": game.ReelStrips[0] = null; break;
                case "line-count": game.Paylines = game.Paylines.Take(19).ToArray(); break;
                case "line-length": game.Paylines[0].Rows = new[] { 1, 1, 1, 1 }; break;
                case "line-row": game.Paylines[0].Rows[0] = 3; break;
                case "line-negative-row": game.Paylines[0].Rows[0] = -1; break;
                case "line-duplicate": game.Paylines[1].Rows = game.Paylines[0].Rows.ToArray(); break;
                case "line-null": game.Paylines[0] = null; break;
                case "bet-divisibility": game.Economy.AvailableBets[0] = 201; break;
                case "bet-zero": game.Economy.AvailableBets[0] = 0; break;
                case "bet-negative": game.Economy.AvailableBets[0] = -20; break;
                case "bet-duplicate": game.Economy.AvailableBets[1] = 200; break;
                case "default-bet": game.Economy.DefaultBet = 220; break;
                case "balance-negative": game.Economy.StartingBalance = -1; break;
                case "paytable-negative": game.Paytable[0].Match3 = -1; break;
                case "paytable-scatter": game.Paytable[0].Symbol = SymbolId.SCATTER; break;
                case "paytable-duplicate": game.Paytable[1].Symbol = SymbolId.S1; break;
                case "paytable-null": game.Paytable[0] = null; break;
                case "grid-reels": game.Grid.ReelCount = 4; break;
                case "grid-rows": game.Grid.VisibleRows = 4; break;
                case "scatter-id": game.Grid.ScatterId = SymbolId.S1; break;
                case "symbol-unknown": game.Grid.Symbols[0].Id = (SymbolId)99; break;
                case "symbol-duplicate": game.Grid.Symbols[1].Id = SymbolId.S1; break;
                case "symbol-count": game.Grid.Symbols[0].RequiredCountPerReel = 5; break;
                case "scatter-count": game.Grid.Symbols[7].RequiredCountPerReel = 2; break;
                case "scatter-threshold": game.Scatter.Awards[0].ScatterCount = 2; break;
                case "scatter-duplicate": game.Scatter.Awards[1].ScatterCount = 3; break;
                case "scatter-negative": game.Scatter.Awards[0].FreeSpins = -1; break;
                case "free-multiplier": game.Scatter.FreeSpinsMultiplier = 0; break;
                case "daily-rewards": game.DailyBonus.Rewards = new long[] { 400 }; break;
                case "daily-negative": game.DailyBonus.Rewards[0] = -1; break;
                case "daily-time": game.DailyBonus.ResetSecondsUtc = 86400; break;
                case "daily-policy": game.DailyBonus.StreakResetBehavior = (StreakResetBehavior)99; break;
                case "jackpot-reference": game.Jackpot.ReferenceBet = 0; break;
                case "jackpot-limit": game.Jackpot.MaxWinsPerWeek = 2; break;
                case "jackpot-negative": game.Jackpot.Amount = -1; break;
                case "jackpot-weekday": game.Jackpot.ResetWeekday = (DayOfWeek)99; break;
                case "jackpot-time": game.Jackpot.ResetSecondsUtc = -1; break;
                case "jackpot-version": game.Jackpot.ConfigVersion = " "; break;
                case "jackpot-id": game.Jackpot.EventId = ""; break;
                case "version": game.ConfigVersion = ""; break;
                case "null-grid": game.Grid = null; break;
                case "null-economy": game.Economy = null; break;
                case "null-scatter": game.Scatter = null; break;
                case "null-daily": game.DailyBonus = null; break;
                case "null-jackpot": game.Jackpot = null; break;
                case "null-debug": game.DebugRandom = null; break;
                default: Assert.Fail("Unknown mutation"); break;
            }
            AssertRejected(field);
        }

        [TestCase(-0.1)]
        [TestCase(1.1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidProbabilitiesAreRejected(double probability)
        {
            game.Jackpot.BaseProbability = probability;
            AssertRejected("Jackpot.BaseProbability");
        }

        [TestCase(0.0)]
        [TestCase(1.0)]
        public void ProbabilityBoundaryValuesAreValid(double probability)
        {
            game.Jackpot.BaseProbability = probability;
            Assert.That(new LocalSlotConfigProvider(game).Load().Jackpot.BaseProbability, Is.EqualTo(probability));
        }

        [TestCase("line-product")]
        [TestCase("free-spin-product")]
        [TestCase("jackpot-addition")]
        [TestCase("balance-addition")]
        [TestCase("daily-addition")]
        public void PotentialInt64OverflowIsRejected(string mutation)
        {
            switch (mutation)
            {
                case "line-product": game.Paytable[0].Match5 = long.MaxValue; break;
                case "free-spin-product": game.Scatter.FreeSpinsMultiplier = long.MaxValue; break;
                case "jackpot-addition": game.Jackpot.Amount = long.MaxValue; break;
                case "balance-addition": game.Economy.StartingBalance = long.MaxValue; break;
                case "daily-addition": game.DailyBonus.Rewards[0] = long.MaxValue; break;
            }
            AssertRejected("PayoutBounds");
        }

        [TestCase("not-a-date")]
        [TestCase("2026-10-12T00:00:00")]
        [TestCase("2026-10-12T00:00:00+03:00")]
        [TestCase("2026-02-30T00:00:00Z")]
        public void InvalidOrNonUtcEventDatesAreRejected(string date)
        {
            game.Jackpot.StartUtc = date;
            AssertRejected("Jackpot.StartUtc");
        }

        [Test]
        public void EventDatesPreserveUtcAndRejectReversedInterval()
        {
            game.Jackpot.StartUtc = "2026-10-12T00:00:00Z";
            game.Jackpot.EndUtc = "2026-10-19T00:00:00+00:00";
            var config = new LocalSlotConfigProvider(game).Load();
            Assert.That(config.Jackpot.StartUtc, Is.EqualTo(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(config.Jackpot.EndUtc.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            game.Jackpot.EndUtc = game.Jackpot.StartUtc;
            AssertRejected("Jackpot.EndUtc");
        }

        [Test]
        public void ConversionSnapshotsDoNotShareMutableData()
        {
            var provider = new LocalSlotConfigProvider(game);
            var original = provider.Load();
            var originalSymbol = original.ReelStrips[0].Symbols[0];
            game.ReelStrips[0].Symbols[0] = SymbolId.SCATTER;
            game.Paylines[0].Rows[0] = 0;
            game.Grid.Symbols[0].RequiredCountPerReel = 1;
            game.Paytable[0].Match3 = 99;
            game.Economy.AvailableBets[0] = 220;
            game.Economy.StartingBalance = 30000;
            game.Scatter.Awards[0].FreeSpins = 99;
            game.DailyBonus.Rewards[0] = 999;
            game.Jackpot.Amount = 999;
            game.ConfigVersion = "new-version";
            game.DebugRandom.UseFixedSeed = true;
            Assert.That(original.ReelStrips[0].Symbols[0], Is.EqualTo(originalSymbol));
            Assert.That(original.Paylines[0].Rows[0], Is.EqualTo(1));
            Assert.That(original.Symbols[0].RequiredCountPerReel, Is.EqualTo(6));
            Assert.That(original.Paytable.Entries[0].Match3, Is.EqualTo(13));
            Assert.That(original.Economy.AvailableBets[0], Is.EqualTo(200));
            Assert.That(original.Economy.StartingBalance, Is.EqualTo(20000));
            Assert.That(original.Scatter.Awards[0].FreeSpins, Is.EqualTo(8));
            Assert.That(original.DailyBonus.Rewards[0], Is.EqualTo(400));
            Assert.That(original.Jackpot.Amount, Is.EqualTo(100000));
            Assert.That(original.ConfigVersion, Is.EqualTo("1.0.0"));
            Assert.That(original.DebugRandom.UseFixedSeed, Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<SymbolId>)original.ReelStrips[0].Symbols)[0] = SymbolId.SCATTER);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)original.Paylines[0].Rows)[0] = 0);
            Assert.Throws<NotSupportedException>(() => ((IList<long>)original.Economy.AvailableBets)[0] = 220);
            Assert.Throws<NotSupportedException>(() => ((IList<long>)original.DailyBonus.Rewards)[0] = 999);
            AssertRejected("ReelStrips[0]");
        }

        [Test]
        public void EditedAssetCreatesNewSnapshotWithoutChangingPreviousSnapshot()
        {
            var provider = new LocalSlotConfigProvider(game);
            var original = provider.Load();
            game.Paytable[0].Match3 = 14;
            game.Jackpot.Amount = 120000;
            game.DailyBonus.StreakResetBehavior = StreakResetBehavior.PreserveStreak;
            var updated = provider.Load();
            Assert.That(updated.Paytable.Entries[0].Match3, Is.EqualTo(14));
            Assert.That(updated.Jackpot.Amount, Is.EqualTo(120000));
            Assert.That(updated.DailyBonus.StreakResetBehavior, Is.EqualTo(StreakResetBehavior.PreserveStreak));
            Assert.That(original.Paytable.Entries[0].Match3, Is.EqualTo(13));
            Assert.That(original.Jackpot.Amount, Is.EqualTo(100000));
            Assert.That(updated, Is.Not.SameAs(original));
            Assert.That(updated.ReelStrips[0], Is.Not.SameAs(original.ReelStrips[0]));
        }

        [TestCase("sprite", "Visual.Symbols[0].Sprite")]
        [TestCase("missing-symbol", "Visual.Symbols")]
        [TestCase("duplicate-symbol", "Visual.Symbols[1].Symbol")]
        [TestCase("unknown-symbol", "Visual.Symbols[0].Symbol")]
        [TestCase("background", "Visual.SlotBackground")]
        [TestCase("frame", "Visual.SlotFrame")]
        [TestCase("duration", "Visual.SpinDurationSeconds")]
        [TestCase("stagger", "Visual.ReelStopStaggerSeconds")]
        [TestCase("effect", "Visual.WinEffectPrefab")]
        [TestCase("audio", "Visual.SpinAudio")]
        public void InvalidVisualConfigurationHasSpecificDiagnostics(string mutation, string field)
        {
            switch (mutation)
            {
                case "sprite": visual.Symbols[0].Sprite = null; break;
                case "missing-symbol": visual.Symbols = visual.Symbols.Take(7).ToArray(); break;
                case "duplicate-symbol": visual.Symbols[1].Symbol = SymbolId.S1; break;
                case "unknown-symbol": visual.Symbols[0].Symbol = (SymbolId)99; break;
                case "background": visual.SlotBackground = null; break;
                case "frame": visual.SlotFrame = null; break;
                case "duration": visual.SpinDurationSeconds = float.NaN; break;
                case "stagger": visual.ReelStopStaggerSeconds = -1; break;
                case "effect": visual.EnableWinEffect = true; visual.WinEffectPrefab = null; break;
                case "audio": visual.EnableSpinAudio = true; visual.SpinAudio = null; break;
            }
            var errors = VisualConfigValidator.Validate(visual, new LocalSlotConfigProvider(game).Load());
            Assert.That(errors.Any(e => e.Contains(field)), Is.True, string.Join("\n", errors));
        }

        [Test]
        public void MissingConfigAssetsFailWithDiagnostics()
        {
            Assert.That(Assert.Throws<ConfigValidationException>(() => new LocalSlotConfigProvider(null).Load()).Message,
                Does.Contain("SlotGameConfigSO"));
            Assert.That(VisualConfigValidator.Validate(null, new LocalSlotConfigProvider(game).Load())[0], Does.Contain("SlotVisualConfigSO"));
            Assert.That(ConfigValidator.Validate(null)[0], Does.Contain("Config"));
        }

        [Test]
        public void DomainAndApplicationHaveNoUnityAssemblyReferences()
        {
            foreach (var assembly in new[] { typeof(SlotRuntimeConfig).Assembly, typeof(ISlotConfigProvider).Assembly })
                Assert.That(assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("UnityEngine") || a.Name.StartsWith("UnityEditor")), Is.False);
        }

        private void AssertRejected(string field)
        {
            var exception = Assert.Throws<ConfigValidationException>(() => new LocalSlotConfigProvider(game).Load());
            Assert.That(exception.Errors.Any(e => e.Contains(field)), Is.True, exception.Message);
        }
    }
}
