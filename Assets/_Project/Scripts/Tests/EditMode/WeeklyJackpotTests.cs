using System;
using System.Collections.Generic;
using System.Linq;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using IceP.Infrastructure.Persistence;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IceP.Tests
{
    public sealed class WeeklyJackpotTests
    {
        private SlotRuntimeConfig config;
        private FakeSaveRepository repository;
        private GameSession session;
        private SpinService spins;
        private WeeklyJackpotService jackpot;
        private DailyBonusService daily;
        private FakeClock clock;
        private Bits bits;
        private Reels reels;
        private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value).ToUniversalTime();

        private sealed class Bits : IRandomSource
        {
            public int Calls, Default;
            public readonly Queue<int> Values = new Queue<int>();
            public Action Before;
            public int NextInt(int min, int max)
            {
                Assert.That(min, Is.Zero); Assert.That(max, Is.EqualTo(2));
                Before?.Invoke(); Calls++;
                return Values.Count == 0 ? Default : Values.Dequeue();
            }
        }
        private sealed class Reels : IRandomSource
        {
            public int Calls;
            public bool Throw;
            public int[] Stops = new int[5];
            public int NextInt(int min, int max)
            {
                if (Throw) throw new InvalidOperationException("Injected reel failure");
                return Stops[Calls++ % 5];
            }
        }

        [SetUp]
        public void Setup()
        {
            var asset = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>(
                "Assets/_Project/Configs/SlotGameConfigSO.asset"));
            try
            {
                foreach (var payout in asset.Paytable)
                { payout.Match3 = 0; payout.Match4 = 0; payout.Match5 = payout.Symbol == SymbolId.S1 ? 50 : 0; }
                config = new LocalSlotConfigProvider(asset).Load();
            }
            finally { Object.DestroyImmediate(asset); }
            clock = new FakeClock(Utc("2026-10-12T12:00:00Z"));
            bits = new Bits(); reels = new Reels();
            for (int r = 0; r < 5; r++)
            {
                var strip = config.ReelStrips[r].Symbols;
                reels.Stops[r] = Enumerable.Range(0, strip.Count).First(i => strip[(i + 1) % strip.Count] == SymbolId.S1 &&
                    strip[i] != SymbolId.S1 && strip[(i + 2) % strip.Count] != SymbolId.S1);
            }
            var profile = InitialProfileFactory.Create(config); profile.SelectedBet = 1000;
            repository = new FakeSaveRepository(profile);
            Recreate();
        }

        private void Recreate()
        {
            session = new GameSession(repository.Load().Data);
            var balance = new BalanceService(config);
            jackpot = new WeeklyJackpotService(session, config, clock, bits);
            spins = new SpinService(session, config, new SlotEngine(config, reels), balance, repository, () => clock.UtcNow, jackpot);
            daily = new DailyBonusService(session, config, balance, repository, clock);
        }
        private void Rules(double p = .0001, long amount = 100000, bool enabled = true, int limit = 1,
            DayOfWeek day = DayOfWeek.Monday, int seconds = 0, string start = null, string end = null,
            string id = "weekly-jackpot", string version = "1.0.0", long reference = 1000)
        {
            config = new SlotRuntimeConfig(config.ConfigVersion, config.ReelCount, config.VisibleRows, config.ScatterId,
                config.Symbols, config.ReelStrips, config.Paylines, config.Paytable, config.Economy, config.Scatter,
                config.DailyBonus, new JackpotRulesConfig(enabled, id, version, amount, p, reference, limit, day,
                    TimeSpan.FromSeconds(seconds), start == null ? (DateTimeOffset?)null : Utc(start),
                    end == null ? (DateTimeOffset?)null : Utc(end)), config.DebugRandom);
            Recreate();
        }
        private void Profile(Action<SaveData> change)
        {
            var state = repository.Load().Data; change(state); repository.Save(state); Recreate();
        }
        private SpinResult Start()
        {
            var result = spins.StartSpin(); Assert.That(result.Success, Is.True, result.Diagnostic); return result.Spin;
        }
        private void Complete(SpinResult spin)
        {
            var result = spins.CompleteSpin(spin.SpinId); Assert.That(result.Success, Is.True, result.Diagnostic);
        }
        private void States(long balance, int wins, DateTimeOffset? week, bool pending = false)
        {
            foreach (var state in new[] { session.Snapshot, repository.Load().Data })
            {
                Assert.That(state.Balance, Is.EqualTo(balance));
                Assert.That(state.JackpotWinsThisWeek, Is.EqualTo(wins));
                Assert.That(state.JackpotWeekId, Is.EqualTo(week));
                Assert.That(state.PendingSpin != null, Is.EqualTo(pending));
                Assert.That(new SaveDataValidator(config).Validate(state), Is.Empty);
            }
        }

        [TestCase("2026-10-12T00:00:00Z", "2026-10-12T00:00:00Z")]
        [TestCase("2026-10-11T23:59:59Z", "2026-10-05T00:00:00Z")]
        [TestCase("2026-11-01T23:59:59Z", "2026-10-26T00:00:00Z")]
        [TestCase("2026-11-02T00:00:00Z", "2026-11-02T00:00:00Z")]
        [TestCase("2027-01-01T00:00:00Z", "2026-12-28T00:00:00Z")]
        [TestCase("2027-01-04T00:00:00Z", "2027-01-04T00:00:00Z")]
        [TestCase("2026-10-12T03:00:00+03:00", "2026-10-12T00:00:00Z")]
        public void WeeklyTiming(string time, string week)
        {
            clock.Now = DateTimeOffset.Parse(time);
            var before = repository.SaveAttemptCount;
            var status = jackpot.GetStatus();
            Assert.That(status.CurrentWeekId, Is.EqualTo(Utc(week)));
            Assert.That(status.NextResetUtc, Is.EqualTo(Utc(week).AddDays(7)));
            Assert.That(status.IsEligible, Is.True);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(before));
            Assert.That(bits.Calls, Is.Zero); States(20000, 0, null);
        }
        [TestCase("2026-10-14T05:59:59Z", "2026-10-07T06:00:00Z")]
        [TestCase("2026-10-14T06:00:00Z", "2026-10-14T06:00:00Z")]
        public void ConfiguredSchedule(string time, string week)
        {
            Rules(day: DayOfWeek.Wednesday, seconds: 21600); clock.Now = Utc(time);
            Assert.That(jackpot.GetStatus().CurrentWeekId, Is.EqualTo(Utc(week)));
            Assert.That(jackpot.GetStatus().NextResetUtc, Is.EqualTo(Utc(week).AddDays(7)));
            Assert.That(Start().Jackpot.WeekId, Is.EqualTo(Utc(week)));
        }
        [TestCase("2026-10-14T11:59:59Z", false)]
        [TestCase("2026-10-14T12:00:00Z", true)]
        [TestCase("2026-10-15T11:59:59Z", true)]
        [TestCase("2026-10-15T12:00:00Z", false)]
        public void EventBoundaries(string now, bool active)
        {
            Rules(start: "2026-10-14T12:00:00Z", end: "2026-10-15T12:00:00Z"); clock.Now = Utc(now);
            Assert.That(jackpot.GetStatus().IsEligible, Is.EqualTo(active));
            Assert.That(Start().Jackpot.Participated, Is.EqualTo(active));
            Assert.That(bits.Calls > 0, Is.EqualTo(active));
        }
        [Test]
        public void DisabledAndFreeSpinsSkipRng()
        {
            Rules(enabled: false); Assert.That(jackpot.GetStatus().State, Is.EqualTo(JackpotState.Inactive));
            var spin = Start(); Assert.That(spin.Jackpot.Participated, Is.False); Complete(spin);
            Profile(s => { s.FreeSpinsRemaining = 2; s.BonusBet = 1000; }); Rules();
            spin = Start(); Assert.That(spin.Jackpot.Participated, Is.False); Assert.That(spin.Jackpot.WeekId, Is.Null);
            Complete(spin); Assert.That(bits.Calls, Is.Zero);
            Assert.That(session.Snapshot.FreeSpinsRemaining, Is.EqualTo(1));
            States(24000, 0, null);
        }
        [Test]
        public void RejectedBusyAndInsufficientSpinsSkipRng()
        {
            Profile(s => s.Balance = 999);
            Assert.That(spins.StartSpin().Error, Is.EqualTo(SpinError.InsufficientBalance)); Assert.That(bits.Calls, Is.Zero);
            Profile(s => s.Balance = 20000); var spin = Start(); int calls = bits.Calls;
            Assert.That(spins.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            Assert.That(spins.SelectBet(123).Success, Is.False); Assert.That(bits.Calls, Is.EqualTo(calls));
            Complete(spin);
        }
        [TestCase(200, .00002)] [TestCase(400, .00004)] [TestCase(600, .00006)]
        [TestCase(800, .00008)] [TestCase(1000, .0001)]
        public void ConfiguredBetProbability(long bet, double expected)
        {
            Assert.That(new JackpotRules(config.Jackpot).CalculateProbability(SpinEvaluationContext.Paid(bet)),
                Is.EqualTo(expected).Within(1e-18));
        }
        [TestCase(0, false)] [TestCase(1, true)]
        public void DeterministicEndpoints(double p, bool win)
        {
            Rules(p: p); var spin = Start(); Assert.That(spin.Jackpot.Participated, Is.True);
            Assert.That(spin.Jackpot.JackpotWon, Is.EqualTo(win)); Assert.That(bits.Calls, Is.Zero);
            Complete(spin); States(win ? 121500 : 21500, win ? 1 : 0, win ? spin.JackpotWeekId : null);
        }
        [Test]
        public void ClampingUsesDomainFormula()
        {
            Rules(p: .75, reference: 200);
            Assert.That(new JackpotRules(config.Jackpot).CalculateProbability(SpinEvaluationContext.Paid(1000)), Is.EqualTo(1));
            Assert.That(Start().Jackpot.JackpotWon, Is.True); Assert.That(bits.Calls, Is.Zero);
        }
        [TestCase(0, true)] [TestCase(1, false)]
        public void HalfProbabilityThreshold(int bit, bool win)
        {
            Rules(p: .5); bits.Default = bit; var spin = Start();
            Assert.That(spin.Jackpot.JackpotWon, Is.EqualTo(win)); Assert.That(bits.Calls, Is.EqualTo(1));
            Complete(spin); Assert.That(bits.Calls, Is.EqualTo(1));
        }
        [Test]
        public void SmallestProbabilityIsNotRoundedToZeroAndEqualityLoses()
        {
            Assert.That(WeeklyJackpotService.Sample(double.Epsilon, bits), Is.True);
            Assert.That(bits.Calls, Is.EqualTo(1074));
            bits.Calls = 0;
            bits.Values.Enqueue(0); bits.Values.Enqueue(0); bits.Values.Enqueue(1);
            Assert.That(WeeklyJackpotService.Sample(.125, bits), Is.False); Assert.That(bits.Calls, Is.EqualTo(3));
        }
        [Test]
        public void DeterministicExampleLimitAndNextWeek()
        {
            bits.Before = () => States(19000, 0, null, true);
            var spin = Start(); bits.Before = null;
            Assert.That(spin.Evaluation.FinalPayout, Is.EqualTo(2500));
            Assert.That(spin.Jackpot.JackpotAmount, Is.EqualTo(100000));
            Assert.That(jackpot.GetStatus().WinsThisWeek, Is.Zero);
            Complete(spin); States(121500, 1, spin.JackpotWeekId);
            Assert.That(session.Snapshot.LastWin, Is.EqualTo(102500));
            int calls = bits.Calls; var second = Start(); Assert.That(second.Jackpot.Participated, Is.False); Complete(second);
            Assert.That(bits.Calls, Is.EqualTo(calls));
            Assert.That(jackpot.GetStatus().State, Is.EqualTo(JackpotState.WonThisWeek));
            Recreate(); Assert.That(jackpot.GetStatus().IsEligible, Is.False);
            clock.Advance(TimeSpan.FromDays(7)); Assert.That(jackpot.GetStatus().IsEligible, Is.True);
            Assert.That(jackpot.GetStatus().WinsThisWeek, Is.Zero);
            Complete(Start()); States(224500, 1, Utc("2026-10-19T00:00:00Z"));
        }
        [Test]
        public void LosingTrialDoesNotConsumeAllowanceOrReelRandomness()
        {
            bits.Default = 1; var spin = Start(); Assert.That(spin.Jackpot.JackpotWon, Is.False);
            Assert.That(reels.Calls, Is.EqualTo(5)); Complete(spin); States(21500, 0, null);
            bits.Default = 0; spin = Start(); Assert.That(spin.Evaluation.FinalPayout, Is.EqualTo(2500));
            Assert.That(spin.Evaluation.Outcome.Stops.Select(s => s.Position), Is.EqualTo(reels.Stops));
            Assert.That(reels.Calls, Is.EqualTo(10)); Complete(spin); States(123000, 1, spin.JackpotWeekId);
        }
        [Test]
        public void ScatterAndDailyBonusArePreserved()
        {
            var claim = daily.Claim(); Assert.That(claim.Success, Is.True);
            for (int r = 0; r < 3; r++) reels.Stops[r] = config.ReelStrips[r].Symbols.ToList().IndexOf(SymbolId.SCATTER);
            var spin = Start(); Assert.That(spin.Evaluation.Scatter.AwardedFreeSpins, Is.GreaterThanOrEqualTo(8));
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy)); Complete(spin);
            foreach (var state in new[] { session.Snapshot, repository.Load().Data })
            {
                Assert.That(state.DailyLastClaimUtc, Is.EqualTo(claim.ClaimUtc)); Assert.That(state.DailyStreakDay, Is.EqualTo(1));
                Assert.That(state.FreeSpinsRemaining, Is.EqualTo(spin.Evaluation.Scatter.AwardedFreeSpins));
                Assert.That(state.BonusBet, Is.EqualTo(1000)); Assert.That(state.JackpotWinsThisWeek, Is.EqualTo(1));
            }
            clock.Advance(TimeSpan.FromDays(1)); Assert.That(daily.Claim().Success, Is.True);
            Assert.That(session.Snapshot.JackpotWeekId, Is.EqualTo(spin.JackpotWeekId));
            Assert.That(repository.Load().Data.JackpotWinsThisWeek, Is.EqualTo(1));
        }
        [TestCase(false)] [TestCase(true)]
        public void FailedDebitNeverDrawsUntilExactRetry(bool committed)
        {
            repository.FailSaves = !committed; repository.CommitThenFail = committed;
            Assert.That(spins.StartSpin().Error, Is.EqualTo(SpinError.PersistenceFailure)); Assert.That(bits.Calls, Is.Zero);
            Assert.That(reels.Calls, Is.Zero); Assert.That(session.Snapshot.Balance, Is.EqualTo(20000));
            clock.Advance(TimeSpan.FromDays(7)); repository.FailSaves = false; repository.CommitThenFail = false;
            var retry = spins.RetryPersistence(); Assert.That(retry.Success, Is.True);
            Assert.That(retry.Spin.JackpotWeekId, Is.EqualTo(Utc("2026-10-12T00:00:00Z")));
            Complete(retry.Spin); States(121500, 1, retry.Spin.JackpotWeekId);
        }
        [TestCase(false)] [TestCase(true)]
        public void FailedSettlementRetryIsExactAndIdempotent(bool committed)
        {
            var spin = Start(); var proposals = new List<SaveData>(); repository.BeforeSave = s => proposals.Add(s);
            repository.FailSaves = !committed; repository.CommitThenFail = committed;
            var failed = spins.CompleteSpin(spin.SpinId); Assert.That(failed.Error, Is.EqualTo(SpinError.PersistenceFailure));
            Assert.That(failed.ConfirmedPayout, Is.Zero); Assert.That(session.Snapshot.Balance, Is.EqualTo(19000));
            Assert.That(session.Snapshot.JackpotWinsThisWeek, Is.Zero); Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(jackpot.GetStatus().WinsThisWeek, Is.Zero);
            int calls = bits.Calls; clock.Advance(TimeSpan.FromDays(7));
            Assert.That(spins.CompleteSpin(spin.SpinId).Success, Is.False);
            repository.FailSaves = false; repository.CommitThenFail = false;
            Assert.That(spins.RetryPersistence().ConfirmedPayout, Is.EqualTo(102500));
            Assert.That(bits.Calls, Is.EqualTo(calls)); States(121500, 1, spin.JackpotWeekId);
            Assert.That(proposals.Count, Is.EqualTo(3));
            foreach (var proposal in proposals)
            {
                Assert.That(proposal.Balance, Is.EqualTo(121500)); Assert.That(proposal.JackpotWinsThisWeek, Is.EqualTo(1));
                Assert.That(proposal.JackpotWeekId, Is.EqualTo(spin.JackpotWeekId));
                Assert.That(proposal.PendingSpin, Is.Null); Assert.That(proposal.LastSettledSpinId, Is.EqualTo(spin.SpinId));
            }
            Assert.That(spins.CompleteSpin(spin.SpinId).AlreadySettled, Is.True); States(121500, 1, spin.JackpotWeekId);
            Recreate(); States(121500, 1, spin.JackpotWeekId);
        }
        [TestCase(false)] [TestCase(true)]
        public void RestartUsesDurableSettlementOrDiscardsPotentialWin(bool committed)
        {
            var spin = Start();
            if (committed) { repository.CommitThenFail = true; spins.CompleteSpin(spin.SpinId); repository.CommitThenFail = false; }
            int calls = bits.Calls; Recreate(); Assert.That(spins.RecoverInterruptedSpin().Success, Is.True);
            States(committed ? 121500 : 19000, committed ? 1 : 0, committed ? spin.JackpotWeekId : null);
            Assert.That(bits.Calls, Is.EqualTo(calls));
            if (!committed) Assert.That(Start().Jackpot.Participated, Is.True);
        }
        [Test]
        public void OutcomeFailureKeepsDebitAndRecoveryDoesNotInventJackpot()
        {
            reels.Throw = true; Assert.That(spins.StartSpin().Error, Is.EqualTo(SpinError.OutcomeGenerationFailure));
            Assert.That(bits.Calls, Is.Zero); States(19000, 0, null, true);
            Assert.That(spins.RecoverInterruptedSpin().Success, Is.True); States(19000, 0, null);
        }
        [Test]
        public void CrossResetUsesAcceptanceAndDoesNotConsumeNewWeek()
        {
            clock.Now = Utc("2026-10-11T23:59:58Z");
            Rules(end: "2026-10-12T00:00:00Z", version: "captured-version"); var spin = Start();
            clock.Now = Utc("2026-10-12T00:00:02Z"); Complete(spin);
            States(121500, 1, Utc("2026-10-05T00:00:00Z"));
            Assert.That(spin.Jackpot.ConfigVersion, Is.EqualTo("captured-version"));
            Assert.That(jackpot.GetStatus().State, Is.EqualTo(JackpotState.Inactive));
            Assert.That(jackpot.GetStatus().WinsThisWeek, Is.Zero);
            Rules(); Assert.That(jackpot.GetStatus().IsEligible, Is.True); Complete(Start());
            States(223000, 1, Utc("2026-10-12T00:00:00Z"));
        }
        [Test]
        public void ClockRollbackAndEventChangesNeverGrantDuplicateAllowance()
        {
            Complete(Start()); clock.Advance(TimeSpan.FromDays(7)); Complete(Start());
            clock.Advance(TimeSpan.FromDays(-7));
            Assert.That(jackpot.GetStatus().IsEligible, Is.False); Assert.That(Start().Jackpot.Participated, Is.False);
        }
        [Test]
        public void ChangedEventScheduleAndLowerLimitPreserveHistoricState()
        {
            Rules(limit: 2); Complete(Start()); Complete(Start());
            Rules(id: "replacement", version: "2", limit: 1, day: DayOfWeek.Tuesday);
            Assert.That(new SaveDataValidator(config).Validate(repository.Load().Data), Is.Empty);
            Assert.That(jackpot.GetStatus().IsEligible, Is.False);
            Assert.That(Start().Jackpot.Participated, Is.False);
            Assert.That(repository.Load().Data.JackpotEventId, Is.EqualTo("weekly-jackpot"));
        }
        [Test]
        public void ConfiguredLimitAllowsExactlyThatManyWins()
        {
            Rules(limit: 2); Complete(Start());
            Assert.That(jackpot.GetStatus().WinsThisWeek, Is.EqualTo(1)); Assert.That(jackpot.GetStatus().IsEligible, Is.True);
            Complete(Start()); States(223000, 2, Utc("2026-10-12T00:00:00Z"));
            int calls = bits.Calls; Assert.That(Start().Jackpot.Participated, Is.False);
            Assert.That(bits.Calls, Is.EqualTo(calls));
        }
        [Test]
        public void InterruptedPendingCanRecoverAfterEventScheduleChange()
        {
            Start(); Rules(id: "new-event", day: DayOfWeek.Wednesday, seconds: 21600);
            Assert.That(spins.RecoverInterruptedSpin().Success, Is.True); States(19000, 0, null);
        }
        [Test]
        public void EventActivityUsesAcceptanceBeforeDebitAndReelCallbacks()
        {
            Rules(start: "2026-10-12T12:00:01Z");
            repository.BeforeSave = _ => clock.Advance(TimeSpan.FromSeconds(2));
            var spin = Start(); Assert.That(spin.Jackpot.Participated, Is.False); Assert.That(bits.Calls, Is.Zero);
            repository.BeforeSave = null; Complete(spin);
        }
        [Test]
        public void EventEndingDuringDebitStillDrawsAndSettlesCapturedEvent()
        {
            Rules(end: "2026-10-12T12:00:01Z");
            repository.BeforeSave = _ => clock.Advance(TimeSpan.FromSeconds(2));
            var spin = Start(); repository.BeforeSave = null;
            Assert.That(spin.Jackpot.JackpotWon, Is.True);
            Assert.That(jackpot.GetStatus().State, Is.EqualTo(JackpotState.Inactive));
            Complete(spin); States(121500, 1, spin.JackpotWeekId);
        }
        [Test]
        public void ActiveCrossResetStatusHasNewAllowanceWhileOldSpinSettles()
        {
            clock.Now = Utc("2026-10-11T23:59:58Z"); var spin = Start();
            clock.Now = Utc("2026-10-12T00:00:02Z"); Complete(spin);
            States(121500, 1, Utc("2026-10-05T00:00:00Z"));
            Assert.That(jackpot.GetStatus().State, Is.EqualTo(JackpotState.Active));
            Assert.That(jackpot.GetStatus().IsEligible, Is.True); Assert.That(jackpot.GetStatus().WinsThisWeek, Is.Zero);
            Complete(Start()); States(223000, 1, Utc("2026-10-12T00:00:00Z"));
        }
        [Test]
        public void JackpotCanWinWithoutLinePayoutAndWithFiveScatters()
        {
            for (int r = 0; r < 5; r++) reels.Stops[r] = config.ReelStrips[r].Symbols.ToList().IndexOf(SymbolId.SCATTER);
            var spin = Start(); Assert.That(spin.Evaluation.FinalPayout, Is.Zero);
            Assert.That(spin.Evaluation.Scatter.AwardedFreeSpins, Is.EqualTo(20)); Complete(spin);
            States(119000, 1, spin.JackpotWeekId);
            foreach (var state in new[] { session.Snapshot, repository.Load().Data })
            {
                Assert.That(state.LastWin, Is.EqualTo(100000)); Assert.That(state.FreeSpinsRemaining, Is.EqualTo(20));
                Assert.That(state.BonusBet, Is.EqualTo(1000)); Assert.That(state.DailyLastClaimUtc, Is.Null);
            }
        }
        [Test]
        public void CapturedConfigurationIsUnaffectedByReplacementSnapshot()
        {
            var spin = Start();
            config = new SlotRuntimeConfig("replacement", config.ReelCount, config.VisibleRows, config.ScatterId,
                config.Symbols, config.ReelStrips, config.Paylines, config.Paytable, config.Economy, config.Scatter,
                config.DailyBonus, new JackpotRulesConfig(false, "different", "2.0.0", 0, 0, 1000, 1,
                    DayOfWeek.Monday, TimeSpan.Zero, null, null), config.DebugRandom);
            Complete(spin); States(121500, 1, spin.JackpotWeekId);
            Assert.That(spin.Configuration.ConfigVersion, Is.EqualTo("1.0.0"));
            Assert.That(spin.Jackpot.ConfigVersion, Is.EqualTo("1.0.0"));
            Assert.That(spin.Jackpot.EventId, Is.EqualTo("weekly-jackpot"));
            Assert.That(repository.Load().Data.JackpotEventId, Is.EqualTo("weekly-jackpot"));
        }
        [Test]
        public void NewerPersistedWeekCannotBeOverwrittenByBackwardSpin()
        {
            Profile(s => { s.JackpotWeekId = Utc("2026-10-19T00:00:00Z"); s.JackpotWinsThisWeek = 1;
                s.JackpotEventId = "weekly-jackpot"; });
            var spin = Start(); Assert.That(spin.Jackpot.Participated, Is.False); Complete(spin);
            States(21500, 1, Utc("2026-10-19T00:00:00Z")); Assert.That(bits.Calls, Is.Zero);
        }
        [Test]
        public void AttachedJackpotCannotBeSilentlyOmittedFromSpinService()
        {
            var state = new GameSession(repository.Load().Data);
            new WeeklyJackpotService(state, config, clock, bits);
            Assert.Throws<ArgumentException>(() => new SpinService(state, config, new SlotEngine(config, reels),
                new BalanceService(config), repository, () => clock.UtcNow));
        }
        [Test]
        public void DedicatedDebugStreamIsReproducibleAndDistinct()
        {
            var settings = new DebugRandomSettings(true, 12345);
            var a = IceP.Infrastructure.Randomness.RandomSourceFactory.CreateJackpot(settings);
            var b = IceP.Infrastructure.Randomness.RandomSourceFactory.CreateJackpot(settings);
            var reel = IceP.Infrastructure.Randomness.RandomSourceFactory.Create(settings);
            var actual = Enumerable.Range(0, 10).Select(_ => a.NextInt(0, 1000000)).ToArray();
            Assert.That(actual, Is.EqualTo(Enumerable.Range(0, 10).Select(_ => b.NextInt(0, 1000000)).ToArray()));
            Assert.That(actual, Is.Not.EqualTo(Enumerable.Range(0, 10).Select(_ => reel.NextInt(0, 1000000)).ToArray()));
        }
        [Test]
        public void LegacyIdentityUsesConservativeAllowance()
        {
            Profile(s => { s.JackpotWeekId = Utc("2026-10-12T00:00:00Z"); s.JackpotWinsThisWeek = 1; });
            Rules(id: "new-event"); Assert.That(jackpot.GetStatus().IsEligible, Is.False);
            Assert.That(Start().Jackpot.Participated, Is.False); Assert.That(bits.Calls, Is.Zero);
        }
        [Test]
        public void ZeroAmountWinStillConsumesAllowance()
        {
            Rules(amount: 0); var spin = Start(); Complete(spin); States(21500, 1, spin.JackpotWeekId);
            Assert.That(session.Snapshot.LastWin, Is.EqualTo(2500));
        }
        [Test]
        public void PayoutOverflowDoesNotSaveOrConfirmReward()
        {
            Profile(s => s.Balance = long.MaxValue); var spin = Start(); int writes = repository.SaveAttemptCount;
            Assert.That(spins.CompleteSpin(spin.SpinId).Error, Is.EqualTo(SpinError.ArithmeticOverflow));
            States(long.MaxValue - 1000, 0, null, true); Assert.That(repository.SaveAttemptCount, Is.EqualTo(writes));
        }
        [TestCase(-.1, 1000)] [TestCase(1.1, 1000)] [TestCase(double.NaN, 1000)]
        [TestCase(double.PositiveInfinity, 1000)] [TestCase(.0001, 0)] [TestCase(.0001, -1)]
        public void InvalidConfigurationRejected(double p, long reference)
        {
            Assert.Throws<ConfigValidationException>(() => Rules(p: p, reference: reference));
        }
        [Test]
        public void OneOwnerAndSharedBindingEnforced()
        {
            Assert.Throws<InvalidOperationException>(() => new WeeklyJackpotService(session, config, clock, bits));
            var other = new GameSession(repository.Load().Data);
            Assert.Throws<ArgumentException>(() => new SpinService(other, config, new SlotEngine(config, reels),
                new BalanceService(config), repository, () => clock.UtcNow, jackpot));
        }
        [Test]
        public void SharedGateRejectsReentrantCommandsAndStatusIsReadOnly()
        {
            clock.BeforeRead = () =>
            {
                Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
                Assert.That(spins.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            };
            Start(); clock.BeforeRead = null;
            int writes = repository.SaveAttemptCount, calls = bits.Calls;
            jackpot.GetStatus(); jackpot.GetStatus();
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(writes)); Assert.That(bits.Calls, Is.EqualTo(calls));
        }
        [Test]
        public void JsonRoundtripAndLegacyOptionalField()
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IceP-Jackpot-" + Guid.NewGuid().ToString("N"));
            try
            {
                Complete(Start()); var json = new JsonSaveRepository(directory, config);
                Assert.That(json.Save(session.Snapshot).Success, Is.True);
                Assert.That(json.Load().Data.JackpotEventId, Is.EqualTo("weekly-jackpot"));
                var path = System.IO.Path.Combine(directory, "save.json");
                var text = System.IO.File.ReadAllText(path);
                text = System.Text.RegularExpressions.Regex.Replace(text, "\\s*\"JackpotEventId\": \"weekly-jackpot\",", "");
                System.IO.File.WriteAllText(path, text);
                var load = json.Load(); Assert.That(load.Status, Is.EqualTo(SaveLoadStatus.Loaded));
                Assert.That(load.Data.JackpotEventId, Is.Null); Assert.That(load.Data.JackpotWinsThisWeek, Is.EqualTo(1));
                Assert.That(load.Data.Balance, Is.EqualTo(121500));
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }
    }
}
