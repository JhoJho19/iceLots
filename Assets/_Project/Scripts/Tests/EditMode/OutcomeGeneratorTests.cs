using System;
using System.Collections.Generic;
using System.Linq;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using IceP.Infrastructure.Randomness;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IceP.Tests
{
    public sealed class OutcomeGeneratorTests
    {
        private SlotGameConfigSO source;
        private SlotRuntimeConfig actual;

        [SetUp]
        public void SetUp()
        {
            source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>(
                "Assets/_Project/Configs/SlotGameConfigSO.asset"));
            actual = new LocalSlotConfigProvider(source).Load();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(source);

        [TestCase(0)]
        [TestCase(28)]
        [TestCase(29)]
        public void SyntheticReelsMapAllRowsAndAllReelsAtBoundaries(int stop)
        {
            // Distinct rotations of a known 30-symbol sequence, preserving required frequencies.
            var sequence = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3, 4, 5, 6,
                0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 0, 1, 2, 0 };
            var reels = Enumerable.Range(0, 5).Select(reel => new ReelStrip(
                Enumerable.Range(0, 30).Select(index => (SymbolId)sequence[(index + reel) % 30]))).ToArray();
            var config = WithReels(reels);
            var rng = new ControlledRandom(Enumerable.Repeat(stop, 5).ToArray());
            var outcome = new OutcomeGenerator(config, rng).Generate();
            Assert.That(outcome.Stops.Count, Is.EqualTo(5));
            for (var reel = 0; reel < 5; reel++)
            {
                Assert.That(outcome.Stops[reel].ReelIndex, Is.EqualTo(reel));
                Assert.That(outcome.Stops[reel].Position, Is.EqualTo(stop));
                for (var row = 0; row < 3; row++)
                    Assert.That(outcome.Grid[reel, row], Is.EqualTo((SymbolId)sequence[(stop + row + reel) % 30]));
            }
            Assert.That(rng.Ranges, Is.EqualTo(Enumerable.Repeat((0, 30), 5)));
        }

        [Test]
        public void ActualConfigPreservesEveryPositionAndMapsExactMixedStopMatrix()
        {
            for (var reel = 0; reel < 5; reel++)
                Assert.That(actual.ReelStrips[reel].Symbols, Is.EqualTo(source.ReelStrips[reel].Symbols));
            var before = actual.ReelStrips.Select(r => r.Symbols.ToArray()).ToArray();
            var rng = new ControlledRandom(0, 28, 29, 27, 5);
            var outcome = new OutcomeGenerator(actual, rng).Generate();
            // Rows top to bottom, reels left to right, pinned to the serialized real asset.
            AssertMatrix(outcome, new[,] { { 3, 0, 3, 7, 7 }, { 1, 1, 0, 5, 0 }, { 0, 4, 0, 2, 3 } });
            Assert.That(outcome.Stops.Select(s => s.Position), Is.EqualTo(new[] { 0, 28, 29, 27, 5 }));
            Assert.That(rng.Ranges, Is.EqualTo(Enumerable.Repeat((0, 30), 5)));
            for (var reel = 0; reel < 5; reel++)
                Assert.That(actual.ReelStrips[reel].Symbols, Is.EqualTo(before[reel]));
        }

        [TestCase(0, "3,4,0,0,4;1,2,0,1,2;0,5,5,0,0")]
        [TestCase(28, "3,0,2,5,1;2,1,3,2,1;3,4,0,0,4")]
        [TestCase(29, "2,1,3,2,1;3,4,0,0,4;1,2,0,1,2")]
        public void ActualConfigBoundaryMatricesAreExact(int stop, string expected)
        {
            var outcome = new OutcomeGenerator(actual, new ControlledRandom(Enumerable.Repeat(stop, 5).ToArray())).Generate();
            var rows = expected.Split(';').Select(r => r.Split(',').Select(int.Parse).ToArray()).ToArray();
            for (var row = 0; row < 3; row++)
                for (var reel = 0; reel < 5; reel++)
                    Assert.That(outcome.Grid[reel, row], Is.EqualTo((SymbolId)rows[row][reel]));
        }

        [Test]
        public void DifferentControlledSequencesProduceTheirCorrespondingGrids()
        {
            var rng = new ControlledRandom(0, 0, 0, 0, 0, 29, 29, 29, 29, 29);
            var generator = new OutcomeGenerator(actual, rng);
            AssertMatrix(generator.Generate(), new[,] { { 3, 4, 0, 0, 4 }, { 1, 2, 0, 1, 2 }, { 0, 5, 5, 0, 0 } });
            AssertMatrix(generator.Generate(), new[,] { { 2, 1, 3, 2, 1 }, { 3, 4, 0, 0, 4 }, { 1, 2, 0, 1, 2 } });
            Assert.That(rng.Ranges.Count, Is.EqualTo(10));
        }

        [Test]
        public void SeededGeneratorsReproduceASequenceAndStayWithinBounds()
        {
            var first = new OutcomeGenerator(actual, new SystemRandomSource(12345));
            var second = new OutcomeGenerator(actual, new SystemRandomSource(12345));
            for (var spin = 0; spin < 100; spin++)
            {
                var a = first.Generate();
                var b = second.Generate();
                Assert.That(a.Stops.Select(s => s.Position), Is.EqualTo(b.Stops.Select(s => s.Position)));
                for (var reel = 0; reel < 5; reel++)
                {
                    Assert.That(a.Stops[reel].Position, Is.InRange(0, 29));
                    for (var row = 0; row < 3; row++)
                    {
                        Assert.That(a.Grid[reel, row], Is.EqualTo(b.Grid[reel, row]));
                        Assert.That(a.Grid[reel, row], Is.EqualTo(actual.ReelStrips[reel].Symbols[(a.Stops[reel].Position + row) % 30]));
                    }
                }
            }
        }

        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void DisabledDeterminismNeverPassesConfiguredSeedToRng(int seed)
        {
            var production = new ControlledRandom(7);
            var constructions = 0;
            var rng = RandomSourceFactory.Create(new DebugRandomSettings(false, seed),
                () => { constructions++; return production; },
                _ => throw new AssertionException("Disabled mode must not call the seeded constructor."));
            Assert.That(rng, Is.SameAs(production));
            Assert.That(rng.NextInt(0, 30), Is.EqualTo(7));
            Assert.That(constructions, Is.EqualTo(1));
            Assert.That(RandomSourceFactory.Create(new DebugRandomSettings(false, seed)), Is.TypeOf<SystemRandomSource>());
        }

        [Test]
        public void EnabledDeterminismUsesExactConfiguredSeed()
        {
            var rng = RandomSourceFactory.Create(new DebugRandomSettings(true, -123),
                () => throw new AssertionException("Enabled mode must call the seeded constructor."),
                seed => { Assert.That(seed, Is.EqualTo(-123)); return new SystemRandomSource(seed); });
            var expected = new SystemRandomSource(-123);
            var realFactory = RandomSourceFactory.Create(new DebugRandomSettings(true, -123));
            for (var i = 0; i < 50; i++)
            {
                var value = expected.NextInt(-5, 30);
                Assert.That(rng.NextInt(-5, 30), Is.EqualTo(value));
                Assert.That(realFactory.NextInt(-5, 30), Is.EqualTo(value));
            }
        }

        [Test]
        public void ConfigurationAndOutcomeDoNotShareMutableCollections()
        {
            var generator = new OutcomeGenerator(actual, new ControlledRandom(0, 28, 29, 27, 5, 0, 28, 29, 27, 5));
            var outcome = generator.Generate();
            source.ReelStrips[0].Symbols[0] = SymbolId.SCATTER;
            source.ReelStrips[1] = null;
            AssertMatrix(outcome, new[,] { { 3, 0, 3, 7, 7 }, { 1, 1, 0, 5, 0 }, { 0, 4, 0, 2, 3 } });
            AssertMatrix(generator.Generate(), new[,] { { 3, 0, 3, 7, 7 }, { 1, 1, 0, 5, 0 }, { 0, 4, 0, 2, 3 } });
            Assert.Throws<NotSupportedException>(() => ((IList<ReelStop>)outcome.Stops)[0] = new ReelStop(0, 1));
            Assert.Throws<NotSupportedException>(() => ((IList<SymbolId>)actual.ReelStrips[0].Symbols)[0] = SymbolId.S7);
            Assert.Throws<NotSupportedException>(() => ((IList<ReelStrip>)actual.ReelStrips)[0] = new ReelStrip(new[] { SymbolId.S7 }));

            var symbols = new SymbolId[5, 3];
            var grid = new SlotGrid(symbols);
            var stops = Enumerable.Range(0, 5).Select(i => new ReelStop(i, i)).ToArray();
            var snapshot = new SpinOutcome(grid, stops);
            symbols[0, 0] = SymbolId.SCATTER;
            stops[0] = new ReelStop(0, 29);
            Assert.That(snapshot.Grid[0, 0], Is.EqualTo(SymbolId.S1));
            Assert.That(snapshot.Stops[0].Position, Is.Zero);
            Assert.That(typeof(SlotGrid).GetProperty("Item").CanWrite, Is.False);
        }

        [TestCase(-1, 0)]
        [TestCase(5, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 3)]
        public void GridRejectsInvalidCoordinates(int reel, int row)
            => Assert.Throws<ArgumentOutOfRangeException>(() => { var symbol = new SlotGrid(new SymbolId[5, 3])[reel, row]; });

        [Test]
        public void ModelsAndGeneratorRejectInvalidInputs()
        {
            Assert.Throws<ArgumentNullException>(() => new SlotGrid(null));
            Assert.Throws<ArgumentException>(() => new SlotGrid(new SymbolId[3, 5]));
            var badSymbols = new SymbolId[5, 3];
            badSymbols[4, 2] = (SymbolId)99;
            Assert.Throws<ArgumentException>(() => new SlotGrid(badSymbols));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReelStop(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReelStop(5, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReelStop(0, -1));
            var grid = new SlotGrid(new SymbolId[5, 3]);
            var stops = Enumerable.Range(0, 5).Select(i => new ReelStop(i, 0)).ToArray();
            Assert.Throws<ArgumentNullException>(() => new SpinOutcome(null, stops));
            Assert.Throws<ArgumentNullException>(() => new SpinOutcome(grid, null));
            Assert.Throws<ArgumentException>(() => new SpinOutcome(grid, stops.Take(4)));
            Assert.Throws<ArgumentException>(() => new SpinOutcome(grid, stops.Reverse()));
            Assert.Throws<ArgumentNullException>(() => new OutcomeGenerator(null, new ControlledRandom()));
            Assert.Throws<ArgumentNullException>(() => new OutcomeGenerator(actual, null));
            Assert.Throws<ConfigValidationException>(() => new OutcomeGenerator(WithReels(actual.ReelStrips.Take(4)), new ControlledRandom()));
            Assert.Throws<ArgumentNullException>(() => RandomSourceFactory.Create(null));
        }

        [TestCase(-1)]
        [TestCase(30)]
        public void GeneratorRejectsRngContractViolation(int stop)
            => Assert.Throws<InvalidOperationException>(() => new OutcomeGenerator(actual, new ControlledRandom(stop)).Generate());

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        public void StandardRngRejectsEmptyOrReversedRange(int min, int max)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new SystemRandomSource(42).NextInt(min, max));

        [Test]
        public void StandardRngHonorsInclusiveLowerAndExclusiveUpperBounds()
        {
            var rng = new SystemRandomSource(42);
            for (var i = 0; i < 100; i++)
            {
                Assert.That(rng.NextInt(-1, 0), Is.EqualTo(-1));
                Assert.That(rng.NextInt(29, 30), Is.EqualTo(29));
                Assert.That(rng.NextInt(-10, 10), Is.InRange(-10, 9));
            }
        }

        private SlotRuntimeConfig WithReels(IEnumerable<ReelStrip> reels) => new SlotRuntimeConfig(
            actual.ConfigVersion, actual.ReelCount, actual.VisibleRows, actual.ScatterId, actual.Symbols,
            reels, actual.Paylines, actual.Paytable, actual.Economy, actual.Scatter, actual.DailyBonus, actual.Jackpot, actual.DebugRandom);

        private static void AssertMatrix(SpinOutcome outcome, int[,] rows)
        {
            for (var row = 0; row < 3; row++)
                for (var reel = 0; reel < 5; reel++)
                    Assert.That(outcome.Grid[reel, row], Is.EqualTo((SymbolId)rows[row, reel]), $"Reel {reel}, row {row}");
        }

        private sealed class ControlledRandom : IRandomSource
        {
            private readonly Queue<int> values;
            public List<(int Min, int Max)> Ranges { get; } = new List<(int, int)>();
            public ControlledRandom(params int[] values) { this.values = new Queue<int>(values); }
            public int NextInt(int minInclusive, int maxExclusive)
            {
                Ranges.Add((minInclusive, maxExclusive));
                return values.Dequeue();
            }
        }
    }
}
