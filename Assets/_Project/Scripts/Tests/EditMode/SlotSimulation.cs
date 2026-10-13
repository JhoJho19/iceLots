using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;

namespace IceP.Tests.Simulation
{
    // Test-only SplitMix64. Specified integer operations and rejection sampling make
    // exact regression sequences independent of System.Random/runtime versions.
    public sealed class SimulationRandom : IRandomSource
    {
        private ulong state;
        public long Calls { get; private set; }
        public SimulationRandom(ulong seed) { state = seed; }
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            ulong bound = (ulong)((long)maxExclusive - minInclusive);
            ulong threshold = unchecked(0UL - bound) % bound;
            ulong value;
            do
            {
                Calls = checked(Calls + 1);
                unchecked
                {
                    state += 0x9E3779B97F4A7C15UL;
                    value = state;
                    value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                    value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                    value ^= value >> 31;
                }
            } while (value < threshold);
            return (int)(minInclusive + (long)(value % bound));
        }
    }

    public sealed class RunningMoments
    {
        public long Count { get; private set; }
        public double Mean { get; private set; }
        private double m2;
        public double Variance => Count > 1 ? m2 / (Count - 1) : 0;
        public double StandardError => Count > 1 ? Math.Sqrt(Variance / Count) : 0;
        public void Add(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Count = checked(Count + 1);
            var delta = value - Mean;
            Mean += delta / Count;
            m2 += delta * (value - Mean);
        }
    }

    public enum JackpotSimulationMode { Disabled, Uncapped, WeeklyCapped }

    public sealed class SlotSimulationOptions
    {
        public long PaidSpins { get; }
        public long Bet { get; }
        public ulong Seed { get; }
        public long MaxBonusChain { get; }
        public JackpotSimulationMode JackpotMode { get; }
        public int Weeks { get; }
        public int PaidSpinsPerWeek { get; }
        public DateTimeOffset StartUtc { get; }
        public DateTimeOffset PaidInstant(long index, JackpotRulesConfig rules)
        {
            if (JackpotMode != JackpotSimulationMode.WeeklyCapped) return StartUtc;
            var firstWeek = WeeklyJackpotService.WeekAt(StartUtc, rules);
            var span = TimeSpan.FromDays(7) - (StartUtc - firstWeek);
            long within = index % PaidSpinsPerWeek;
            long ticks = span.Ticks / PaidSpinsPerWeek * within +
                span.Ticks % PaidSpinsPerWeek * within / PaidSpinsPerWeek;
            return StartUtc.AddDays(7L * (index / PaidSpinsPerWeek))
                .AddTicks(ticks);
        }
        public SlotSimulationOptions(long paidSpins, long bet = 1000, ulong seed = 412,
            long maxBonusChain = 100000, JackpotSimulationMode jackpotMode = JackpotSimulationMode.Disabled,
            int weeks = 100, int paidSpinsPerWeek = 10000, DateTimeOffset? startUtc = null)
        {
            if (paidSpins <= 0 || maxBonusChain <= 0 || bet <= 0 || weeks <= 0 || paidSpinsPerWeek <= 0 ||
                !Enum.IsDefined(typeof(JackpotSimulationMode), jackpotMode))
                throw new ArgumentOutOfRangeException(nameof(paidSpins), "Counts, bet and chain guard must be positive; mode must be valid.");
            if (jackpotMode == JackpotSimulationMode.WeeklyCapped && paidSpins != checked((long)weeks * paidSpinsPerWeek))
                throw new ArgumentException("Weekly mode requires PaidSpins = Weeks * PaidSpinsPerWeek.");
            checked { var wager = paidSpins * bet; }
            PaidSpins = paidSpins; Bet = bet; Seed = seed; MaxBonusChain = maxBonusChain;
            JackpotMode = jackpotMode; Weeks = weeks; PaidSpinsPerWeek = paidSpinsPerWeek;
            StartUtc = (startUtc ?? new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero)).ToUniversalTime();
        }
    }

    public sealed class SlotSimulationResult
    {
        public SlotRuntimeConfig Config { get; internal set; }
        public SlotSimulationOptions Options { get; internal set; }
        public long PaidSpins, Wager, BasePayout, FreePayout, WinningPaidSpins, WinningLines;
        public long BonusTriggers, InitialFreeSpins, FreeSpins, Retriggers, RetriggerAwards;
        public long MaxPaidPayout, MaxBonusPayout, LongestChain;
        public long JackpotDraws, JackpotWins, JackpotPayout, JackpotRandomCalls;
        public double Seconds;
        public readonly long[] ScatterCounts = new long[6];
        public readonly long[] PaylineWins = new long[20];
        public readonly long[] SymbolWins = new long[7];
        public readonly long[] MatchWins = new long[6];
        public readonly SortedDictionary<long, long> PaidPayoutDistribution = new SortedDictionary<long, long>();
        public readonly SortedDictionary<long, long> ChainLengths = new SortedDictionary<long, long>();
        public readonly SortedDictionary<DateTimeOffset, long> WeeklyWins = new SortedDictionary<DateTimeOffset, long>();
        public readonly RunningMoments BaseReturns = new RunningMoments();
        public readonly RunningMoments FreeReturns = new RunningMoments();
        public readonly RunningMoments SlotReturns = new RunningMoments();
        public readonly RunningMoments TotalReturns = new RunningMoments();
        public readonly RunningMoments InitialAwards = new RunningMoments();
        public readonly RunningMoments ChainSpins = new RunningMoments();
        public double BaseRtp => BasePayout / (double)Wager;
        public double FreeRtp => FreePayout / (double)Wager;
        public double SlotRtp => checked(BasePayout + FreePayout) / (double)Wager;
        public double JackpotRtp => JackpotPayout / (double)Wager;
        internal static void Increment<TKey>(IDictionary<TKey, long> counts, TKey key)
        { counts.TryGetValue(key, out var count); counts[key] = checked(count + 1); }
    }

    public static class SlotSimulationRunner
    {
        public static SlotSimulationResult Run(SlotRuntimeConfig config, SlotSimulationOptions options,
            IRandomSource reelRandom = null, IRandomSource jackpotRandom = null)
        {
            ConfigValidator.ValidateAndThrow(config);
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!config.Economy.AvailableBets.Contains(options.Bet)) throw new ArgumentOutOfRangeException(nameof(options), "Bet must be configured.");
            if (reelRandom != null && ReferenceEquals(reelRandom, jackpotRandom))
                throw new ArgumentException("Reels and jackpot require separate RNG streams.");
            // Reject a mathematically non-convergent bonus before running.
            SlotAnalyticalModel.Calculate(config);
            var timer = Stopwatch.StartNew();
            var engine = new SlotEngine(config, reelRandom ?? new SimulationRandom(options.Seed));
            var bits = jackpotRandom ?? new SimulationRandom(options.Seed ^ 0xD1B54A32D192ED03UL);
            var result = new SlotSimulationResult { Config = config, Options = options };
            var probability = new JackpotRules(config.Jackpot);
            // Only the optional weekly adapter owns a synthetic, private profile. No repository or real session.
            var profile = InitialProfileFactory.Create(config);
            var clock = new FakeClock(options.StartUtc);
            WeeklyJackpotService weekly = options.JackpotMode == JackpotSimulationMode.WeeklyCapped ?
                new WeeklyJackpotService(new GameSession(profile), config, clock, bits) : null;
            for (long index = 0; index < options.PaidSpins; index++)
            {
                checked
                {
                    var paid = engine.Generate(SpinEvaluationContext.Paid(options.Bet));
                    result.PaidSpins++; result.Wager += options.Bet; result.BasePayout += paid.FinalPayout;
                    result.ScatterCounts[paid.Scatter.ScatterCount]++;
                    if (paid.FinalPayout > 0) result.WinningPaidSpins++;
                    result.MaxPaidPayout = Math.Max(result.MaxPaidPayout, paid.FinalPayout);
                    SlotSimulationResult.Increment(result.PaidPayoutDistribution, paid.FinalPayout);
                    foreach (var line in paid.Wins.WinningLines)
                    {
                        result.WinningLines++; result.PaylineWins[line.PaylineIndex]++;
                        result.SymbolWins[(int)line.Symbol]++; result.MatchWins[line.MatchCount]++;
                    }
                    long remaining = paid.Scatter.AwardedFreeSpins, chain = 0, bonusPayout = 0;
                    result.InitialFreeSpins += remaining;
                    result.InitialAwards.Add(remaining);
                    if (remaining > 0) result.BonusTriggers++;
                    while (remaining > 0)
                    {
                        if (chain >= options.MaxBonusChain) throw new InvalidOperationException("Bonus-chain safety guard exceeded; no valid RTP result.");
                        remaining--; chain++; result.FreeSpins++;
                        var free = engine.Generate(SpinEvaluationContext.Free(options.Bet));
                        bonusPayout += free.FinalPayout; result.FreePayout += free.FinalPayout;
                        remaining += free.Scatter.AwardedFreeSpins;
                        if (free.Scatter.AwardedFreeSpins > 0)
                        { result.Retriggers++; result.RetriggerAwards += free.Scatter.AwardedFreeSpins; }
                    }
                    if (chain > 0) SlotSimulationResult.Increment(result.ChainLengths, chain);
                    result.LongestChain = Math.Max(result.LongestChain, chain);
                    result.MaxBonusPayout = Math.Max(result.MaxBonusPayout, bonusPayout);
                    result.ChainSpins.Add(chain);
                    long jackpotPayout = 0;
                    if (options.JackpotMode == JackpotSimulationMode.Uncapped)
                    {
                        double p = probability.CalculateProbability(SpinEvaluationContext.Paid(options.Bet), acceptedAtUtc: options.StartUtc);
                        bool active = config.Jackpot.Enabled && (!config.Jackpot.StartUtc.HasValue || options.StartUtc >= config.Jackpot.StartUtc) &&
                            (!config.Jackpot.EndUtc.HasValue || options.StartUtc < config.Jackpot.EndUtc);
                        if (active)
                        {
                            result.JackpotDraws++;
                            if (WeeklyJackpotService.Sample(p, bits)) { result.JackpotWins++; jackpotPayout = config.Jackpot.Amount; }
                        }
                    }
                    else if (weekly != null)
                    {
                        // StartUtc is in the first synthetic week; spread spins uniformly inside each week.
                        clock.Now = options.PaidInstant(index, config.Jackpot);
                        var week = WeeklyJackpotService.WeekAt(clock.Now, config.Jackpot);
                        if (!result.WeeklyWins.ContainsKey(week)) result.WeeklyWins.Add(week, 0);
                        var pending = new PendingSpin { SpinId = new Guid("00000000-0000-0000-0000-000000000412"),
                            Type = PendingSpinType.Paid, EffectiveBet = options.Bet, AcceptedUtc = clock.Now,
                            ConfigVersion = config.ConfigVersion, JackpotWeekId = week, JackpotEventId = config.Jackpot.EventId };
                        profile.PendingSpin = pending;
                        var draw = weekly.Draw(pending, profile);
                        if (draw.Participated) result.JackpotDraws++;
                        if (draw.JackpotWon)
                        {
                            weekly.ApplyWin(profile, draw); result.JackpotWins++;
                            result.WeeklyWins[week]++; jackpotPayout = draw.JackpotAmount;
                        }
                        profile.PendingSpin = null;
                    }
                    result.JackpotPayout += jackpotPayout;
                    result.BaseReturns.Add(paid.FinalPayout / (double)options.Bet);
                    result.FreeReturns.Add(bonusPayout / (double)options.Bet);
                    result.SlotReturns.Add((paid.FinalPayout + bonusPayout) / (double)options.Bet);
                    result.TotalReturns.Add((paid.FinalPayout + bonusPayout + jackpotPayout) / (double)options.Bet);
                }
            }
            result.JackpotRandomCalls = bits is SimulationRandom rng ? rng.Calls : -1;
            timer.Stop(); result.Seconds = timer.Elapsed.TotalSeconds;
            return result;
        }
    }
}
