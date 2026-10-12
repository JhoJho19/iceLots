using System;
using System.Linq;
using IceP.Domain.Configuration;
using IceP.Infrastructure.Configuration;
using NUnit.Framework;
using UnityEditor;

namespace IceP.Tests
{
    public sealed class SlotMathematicsTests
    {
        [Test]
        public void DefaultMathematicsIsDerivedFromActualStripsPaylinesAndRewards()
        {
            var config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>(
                "Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            double baseRtp = 0;
            // Enumerate each line's marginal symbol probability at each reel/row.
            // Reels are independent. Cells within one reel are not independent.
            foreach (var line in config.Paylines)
            {
                foreach (var payout in config.Paytable.Entries)
                {
                    var p = Enumerable.Range(0, 5).Select(reel =>
                    {
                        var strip = config.ReelStrips[reel].Symbols;
                        return Enumerable.Range(0, strip.Count).Count(stop =>
                            strip[(stop + line.Rows[reel]) % strip.Count] == payout.Symbol) / (double)strip.Count;
                    }).ToArray();
                    var firstThree = p[0] * p[1] * p[2];
                    var exactThree = firstThree * (1 - p[3]);
                    var exactFour = firstThree * p[3] * (1 - p[4]);
                    var five = firstThree * p[3] * p[4];
                    baseRtp += (exactThree * payout.Match3 + exactFour * payout.Match4 + five * payout.Match5)
                        / config.Paylines.Count;
                }
            }

            // Independently enumerate every stop's visible Scatter count, then convolve reels.
            var distribution = new[] { 1.0 };
            foreach (var strip in config.ReelStrips)
            {
                var reelDistribution = new double[config.VisibleRows + 1];
                for (var stop = 0; stop < strip.Symbols.Count; stop++)
                {
                    var count = Enumerable.Range(0, config.VisibleRows).Count(row =>
                        strip.Symbols[(stop + row) % strip.Symbols.Count] == config.ScatterId);
                    reelDistribution[count] += 1.0 / strip.Symbols.Count;
                }
                Assert.That(reelDistribution[1], Is.EqualTo(0.1).Within(1e-12));
                var next = new double[distribution.Length + reelDistribution.Length - 1];
                for (var oldCount = 0; oldCount < distribution.Length; oldCount++)
                    for (var newCount = 0; newCount < reelDistribution.Length; newCount++)
                        next[oldCount + newCount] += distribution[oldCount] * reelDistribution[newCount];
                distribution = next;
            }
            Assert.That(distribution.Sum(), Is.EqualTo(1).Within(1e-12));
            var triggerProbability = config.Scatter.Awards.Where(a => a.FreeSpins > 0).Sum(a => distribution[a.ScatterCount]);
            var initialSpins = config.Scatter.Awards.Sum(a => distribution[a.ScatterCount] * a.FreeSpins);
            var retriggerSpins = config.Scatter.RetriggerEnabled ? initialSpins : 0;
            Assert.That(retriggerSpins, Is.LessThan(1), "Expected bonus-chain length must converge.");
            var totalBonusSpins = initialSpins / (1 - retriggerSpins);
            var withFreeRtp = baseRtp * (1 + totalBonusSpins * config.Scatter.FreeSpinsMultiplier);

            Assert.That(baseRtp, Is.EqualTo(0.856059).Within(0.0000005));
            Assert.That(triggerProbability, Is.EqualTo(0.00856).Within(1e-12));
            Assert.That(initialSpins, Is.EqualTo(0.0704).Within(1e-12));
            Assert.That(withFreeRtp, Is.EqualTo(0.920889).Within(0.0000005));
            TestContext.WriteLine($"Base RTP: {baseRtp:P9}; Scatter 3+: {triggerProbability:R}; newly awarded FS: {initialSpins:R}; total bonus spins: {totalBonusSpins:R}; RTP with retriggers: {withFreeRtp:P9}");

            foreach (var bet in config.Economy.AvailableBets)
            {
                var probability = Math.Min(1, config.Jackpot.BaseProbability * bet / config.Jackpot.ReferenceBet);
                var eligibleSpinContribution = probability * config.Jackpot.Amount / bet;
                Assert.That(eligibleSpinContribution, Is.EqualTo(0.01).Within(1e-12));
                TestContext.WriteLine($"Jackpot contribution on eligible paid spins, bet {bet}: {eligibleSpinContribution:P4}; weekly-limited long-term contribution depends on participation and is not fixed.");
            }
        }
    }
}
