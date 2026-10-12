using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using NUnit.Framework;
using UnityEditor;

namespace IceP.Tests
{
    public sealed class DailyBonusTests
    {
        private SlotRuntimeConfig config;
        private GameSession session;
        private FakeSaveRepository repository;
        private FakeClock clock;
        private DailyBonusService daily;
        private SpinService spins;
        private static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
            new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
        private sealed class FixedRandom : IRandomSource
        {
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }

        [SetUp]
        public void Setup()
        {
            config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>(
                "Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            clock = new FakeClock(Utc(2026, 10, 12, 10));
            repository = new FakeSaveRepository(InitialProfileFactory.Create(config));
            Recreate();
        }
        private void Recreate()
        {
            session = new GameSession(repository.Load().Data);
            var balance = new BalanceService(config);
            spins = new SpinService(session, config, new SlotEngine(config, new FixedRandom()), balance, repository, () => clock.UtcNow);
            daily = new DailyBonusService(session, config, balance, repository, clock);
        }
        private void Rules(bool enabled = true, TimeSpan? reset = null,
            StreakResetBehavior policy = StreakResetBehavior.ResetAfterMissedDay, long[] rewards = null)
        {
            config = WithRules(new DailyBonusRules(enabled, rewards ?? new long[] { 400, 600, 800, 1000, 1200, 1600, 3000 },
                reset ?? TimeSpan.Zero, policy));
            Recreate();
        }
        private SlotRuntimeConfig WithRules(DailyBonusRules rules) => new SlotRuntimeConfig(config.ConfigVersion,
            config.ReelCount, config.VisibleRows, config.ScatterId, config.Symbols, config.ReelStrips,
            config.Paylines, config.Paytable, config.Economy, config.Scatter, rules, config.Jackpot, config.DebugRandom);
        private void Consistent(long coins, int streak, DateTimeOffset? last)
        {
            foreach (var state in new[] { session.Snapshot, repository.Load().Data })
            {
                Assert.That(state.Balance, Is.EqualTo(coins));
                Assert.That(state.DailyStreakDay, Is.EqualTo(streak));
                Assert.That(state.DailyLastClaimUtc, Is.EqualTo(last));
                Assert.That(new SaveDataValidator(config).Validate(state), Is.Empty);
            }
        }

        [Test]
        public void FirstClaimPersistsCompleteSnapshotAndConfirmedResult()
        {
            repository.BeforeSave = proposed =>
            {
                Assert.That(session.State, Is.EqualTo(GameSessionState.Preparing));
                Assert.That(session.Snapshot.Balance, Is.EqualTo(20000));
                Assert.That(session.Snapshot.DailyLastClaimUtc, Is.Null);
                Assert.That(proposed.Balance, Is.EqualTo(20400));
                Assert.That(proposed.DailyLastClaimUtc, Is.EqualTo(clock.Now));
                Assert.That(proposed.DailyStreakDay, Is.EqualTo(1));
            };
            var result = daily.Claim();
            Assert.That(result.Success, Is.True, result.Diagnostic);
            Assert.That(result.RewardDay, Is.EqualTo(1));
            Assert.That(result.AwardedCoins, Is.EqualTo(400));
            Assert.That(result.ConfirmedBalance, Is.EqualTo(20400));
            Assert.That(result.ClaimUtc, Is.EqualTo(clock.Now));
            Assert.That(clock.ReadCount, Is.EqualTo(1));
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Idle));
            Consistent(20400, 1, clock.Now);
        }
        [Test]
        public void ConfiguredSequenceAdvancesThroughSevenDaysAndWraps()
        {
            var rewards = new long[] { 11, 22, 33, 44, 55, 66, 77 };
            Rules(rewards: rewards);
            long coins = 20000;
            for (int i = 0; i < 15; i++)
            {
                int day = i % 7 + 1;
                Assert.That(daily.GetStatus().RewardDay, Is.EqualTo(day));
                var claim = daily.Claim();
                Assert.That(claim.Success, Is.True);
                Assert.That(claim.RewardDay, Is.EqualTo(day));
                Assert.That(claim.AwardedCoins, Is.EqualTo(rewards[day - 1]));
                coins += rewards[day - 1];
                Consistent(coins, day, clock.Now);
                clock.Advance(TimeSpan.FromDays(1));
            }
        }
        [Test]
        public void DisabledDoesNotReadClaimClockOrSave()
        {
            Rules(enabled: false);
            Assert.That(daily.GetStatus().Enabled, Is.False);
            int reads = clock.ReadCount;
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Disabled));
            Assert.That(clock.ReadCount, Is.EqualTo(reads));
            Assert.That(repository.SaveAttemptCount, Is.Zero);
            Consistent(20000, 0, null);
        }
        [Test]
        public void StatusIsReadOnlyAndImmutableBeforeAndAfterClaim()
        {
            var before = daily.GetStatus();
            for (int i = 0; i < 10; i++) Assert.That(daily.GetStatus().CanClaim, Is.True);
            Assert.That(repository.SaveAttemptCount, Is.Zero);
            Consistent(20000, 0, null);
            Assert.Throws<NotSupportedException>(() => ((IList<long>)before.Rewards)[0] = 999);
            daily.Claim();
            for (int i = 0; i < 10; i++)
            {
                var status = daily.GetStatus();
                Assert.That(status.UnavailabilityReason, Is.EqualTo(DailyBonusError.AlreadyClaimed));
                Assert.That(status.RewardDay, Is.EqualTo(2));
                Assert.That(status.RewardAmount, Is.EqualTo(600));
                Assert.That(status.CurrentStreakDay, Is.EqualTo(1));
                Assert.That(status.NextAvailableUtc, Is.EqualTo(Utc(2026, 10, 13)));
            }
            Assert.That(before.CurrentStreakDay, Is.Zero);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
            Consistent(20400, 1, clock.Now);
        }
        [Test]
        public void RapidDuplicateClaimDoesNotChangeTimestampOrSave()
        {
            daily.Claim();
            var timestamp = clock.Now;
            clock.Advance(TimeSpan.FromHours(2));
            var second = daily.Claim();
            Assert.That(second.Error, Is.EqualTo(DailyBonusError.AlreadyClaimed));
            Assert.That(second.AwardedCoins, Is.Zero);
            Assert.That(second.ClaimUtc, Is.Null);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
            Consistent(20400, 1, timestamp);
        }
        [TestCase(0)]
        [TestCase(6)]
        public void OneSecondBeforeAndExactConfiguredReset(int resetHour)
        {
            Rules(reset: TimeSpan.FromHours(resetHour));
            clock.Now = Utc(2026, 10, 12, resetHour);
            daily.Claim();
            var first = clock.Now;
            var next = first.AddDays(1);
            clock.Now = next.AddSeconds(-1);
            Assert.That(daily.GetStatus().NextResetUtc, Is.EqualTo(next));
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.AlreadyClaimed));
            Consistent(20400, 1, first);
            clock.Now = next;
            Assert.That(daily.GetStatus().CanClaim, Is.True);
            Assert.That(daily.Claim().RewardDay, Is.EqualTo(2));
            Consistent(21000, 2, next);
        }
        [Test]
        public void NonMidnightBoundaryIgnoresCalendarMidnightAndSupportsFractionalReset()
        {
            Rules(reset: new TimeSpan(6, 30, 15));
            clock.Now = Utc(2026, 10, 12, 23);
            daily.Claim();
            var first = clock.Now;
            clock.Now = Utc(2026, 10, 13);
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.AlreadyClaimed));
            Assert.That(daily.GetStatus().NextAvailableUtc, Is.EqualTo(Utc(2026, 10, 13, 6, 30, 15)));
            Consistent(20400, 1, first);
            clock.Now = Utc(2026, 10, 13, 6, 30, 15);
            Assert.That(daily.Claim().RewardDay, Is.EqualTo(2));
            Consistent(21000, 2, clock.Now);
        }
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(365)]
        public void MissedOpportunitiesResetStreak(int skippedGap)
        {
            daily.Claim();
            clock.Advance(TimeSpan.FromDays(1));
            daily.Claim();
            clock.Advance(TimeSpan.FromDays(skippedGap));
            Assert.That(daily.GetStatus().RewardDay, Is.EqualTo(1));
            Assert.That(daily.Claim().RewardDay, Is.EqualTo(1));
            Consistent(21400, 1, clock.Now);
        }
        [Test]
        public void PreserveStreakPolicyAdvancesOneRewardAfterMissedDays()
        {
            Rules(policy: StreakResetBehavior.PreserveStreak);
            daily.Claim();
            clock.Advance(TimeSpan.FromDays(10));
            Assert.That(daily.Claim().RewardDay, Is.EqualTo(2));
            Consistent(21000, 2, clock.Now);
        }
        [TestCase(2026, 10, 31)]
        [TestCase(2026, 12, 31)]
        [TestCase(2028, 2, 28)]
        [TestCase(2028, 2, 29)]
        [TestCase(2027, 2, 28)]
        public void CalendarTransitionsAreConsecutive(int year, int month, int day)
        {
            clock.Now = Utc(year, month, day, 23, 59, 59);
            daily.Claim();
            var next = new DateTimeOffset(clock.Now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
            Assert.That(daily.GetStatus().NextResetUtc, Is.EqualTo(next));
            clock.Now = next;
            Assert.That(daily.Claim().RewardDay, Is.EqualTo(2));
            Consistent(21000, 2, next);
        }
        [Test]
        public void OffsetClockIsNormalizedToUtcRewardPeriodAndSavedTimestamp()
        {
            clock.Now = new DateTimeOffset(2026, 10, 13, 2, 0, 0, TimeSpan.FromHours(3));
            var result = daily.Claim();
            Assert.That(result.ClaimUtc, Is.EqualTo(Utc(2026, 10, 12, 23)));
            Assert.That(result.ClaimUtc.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(daily.GetStatus().NextAvailableUtc, Is.EqualTo(Utc(2026, 10, 13)));
            clock.Now = Utc(2026, 10, 13);
            Assert.That(daily.Claim().RewardDay, Is.EqualTo(2));
            Consistent(21000, 2, clock.Now);
        }
        [TestCase(-1)]
        [TestCase(-86400)]
        [TestCase(-864000)]
        public void ClockRollbackDoesNotChangeConfirmedClaim(int seconds)
        {
            daily.Claim();
            var claimed = clock.Now;
            clock.Advance(TimeSpan.FromSeconds(seconds));
            Assert.That(daily.GetStatus().UnavailabilityReason, Is.EqualTo(DailyBonusError.ClockInconsistent));
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.ClockInconsistent));
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
            Consistent(20400, 1, claimed);
            clock.Now = claimed.AddDays(1);
            Assert.That(daily.Claim().RewardDay, Is.EqualTo(2));
            Consistent(21000, 2, clock.Now);
        }
        [Test]
        public void RestartRetainsClaimAndRejectsSamePeriod()
        {
            daily.Claim();
            Recreate();
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.AlreadyClaimed));
            Consistent(20400, 1, clock.Now);
            clock.Advance(TimeSpan.FromDays(1));
            Assert.That(daily.Claim().RewardDay, Is.EqualTo(2));
            Consistent(21000, 2, clock.Now);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void FailedSaveBlocksAllWritersAndRetriesExactSnapshotWithoutClock(bool committed)
        {
            repository.FailSaves = !committed;
            repository.CommitThenFail = committed;
            var timestamp = clock.Now;
            var result = daily.Claim();
            Assert.That(result.Error, Is.EqualTo(DailyBonusError.PersistenceFailure));
            Assert.That(result.AwardedCoins, Is.Zero);
            Assert.That(result.ClaimUtc, Is.Null);
            Assert.That(result.ConfirmedBalance, Is.EqualTo(20000));
            Assert.That(session.Snapshot.Balance, Is.EqualTo(20000));
            Assert.That(session.Snapshot.DailyStreakDay, Is.Zero);
            Assert.That(session.Snapshot.DailyLastClaimUtc, Is.Null);
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(committed ? 20400 : 20000));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(daily.GetStatus().UnavailabilityReason, Is.EqualTo(DailyBonusError.Busy));
            Assert.That(spins.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            Assert.That(spins.SelectBet(1000).Error, Is.EqualTo(SpinError.Busy));
            Assert.That(spins.RetryPersistence().Success, Is.False);
            Assert.That(spins.RecoverInterruptedSpin().Success, Is.False);
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
            Assert.That(session.CanNavigateBack, Is.False);
            repository.BeforeSave = next =>
            {
                Assert.That(next.Balance, Is.EqualTo(20400));
                Assert.That(next.DailyStreakDay, Is.EqualTo(1));
                Assert.That(next.DailyLastClaimUtc, Is.EqualTo(timestamp));
            };
            clock.Advance(TimeSpan.FromDays(4));
            int reads = clock.ReadCount;
            Assert.That(daily.RetryPersistence().Success, Is.False); // repeated failure preserves proposal
            repository.FailSaves = repository.CommitThenFail = false;
            var retry = daily.RetryPersistence();
            Assert.That(retry.Success, Is.True, retry.Diagnostic);
            Assert.That(retry.AwardedCoins, Is.EqualTo(400));
            Assert.That(retry.ClaimUtc, Is.EqualTo(timestamp));
            Assert.That(clock.ReadCount, Is.EqualTo(reads));
            Consistent(20400, 1, timestamp);
            Assert.That(daily.RetryPersistence().Error, Is.EqualTo(DailyBonusError.InvalidState));
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(3));
        }
        [Test]
        public void RestartAfterAmbiguousCommitPreventsDuplicate()
        {
            repository.CommitThenFail = true;
            daily.Claim();
            repository.CommitThenFail = false;
            Recreate();
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.AlreadyClaimed));
            Consistent(20400, 1, clock.Now);
        }
        [Test]
        public void RepositoryExceptionRetainsProposalForRetry()
        {
            repository.BeforeSave = _ => throw new System.IO.IOException("Injected failure.");
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.PersistenceFailure));
            Consistent(20000, 0, null);
            repository.BeforeSave = null;
            Assert.That(daily.RetryPersistence().Success, Is.True);
            Consistent(20400, 1, clock.Now);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ClaimsAndRetriesPreserveAllOtherProfileFields(bool fail)
        {
            var profile = repository.Load().Data;
            profile.FreeSpinsRemaining = 3; profile.BonusBet = 600;
            profile.JackpotWeekId = Utc(2026, 10, 12); profile.JackpotWinsThisWeek = 1;
            profile.LastSettledSpinId = Guid.NewGuid(); profile.LastWin = 1234;
            profile.Settings.MusicEnabled = false; profile.Settings.MusicVolume = 0.25;
            profile.SelectedBet = 800;
            repository = new FakeSaveRepository(profile); Recreate();
            repository.CommitThenFail = fail;
            daily.Claim();
            if (fail)
            {
                Assert.That(session.Snapshot.Balance, Is.EqualTo(20000));
                repository.CommitThenFail = false;
                daily.RetryPersistence();
            }
            Consistent(20400, 1, clock.Now);
            foreach (var state in new[] { session.Snapshot, repository.Load().Data })
            {
                Assert.That(state.FreeSpinsRemaining, Is.EqualTo(3));
                Assert.That(state.BonusBet, Is.EqualTo(600));
                Assert.That(state.JackpotWeekId, Is.EqualTo(profile.JackpotWeekId));
                Assert.That(state.JackpotWinsThisWeek, Is.EqualTo(1));
                Assert.That(state.LastSettledSpinId, Is.EqualTo(profile.LastSettledSpinId));
                Assert.That(state.LastWin, Is.EqualTo(1234));
                Assert.That(state.SelectedBet, Is.EqualTo(800));
                Assert.That(state.Settings.MusicEnabled, Is.False);
                Assert.That(state.Settings.MusicVolume, Is.EqualTo(0.25));
            }
        }
        [Test]
        public void ConcurrentAndReentrantClaimsAndSpinsAreRejectedDuringWrite()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            repository.BeforeSave = _ =>
            {
                Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
                Assert.That(daily.RetryPersistence().Error, Is.EqualTo(DailyBonusError.Busy));
                Assert.That(spins.StartSpin().Error, Is.EqualTo(SpinError.Busy));
                entered.Set();
                if (!release.Wait(5000)) throw new TimeoutException();
            };
            var task = Task.Run(() => daily.Claim());
            try
            {
                Assert.That(entered.Wait(5000), Is.True);
                Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
                Assert.That(spins.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            }
            finally { release.Set(); }
            Assert.That(task.Wait(5000), Is.True);
            Assert.That(task.Result.Success, Is.True, task.Result.Diagnostic);
            Consistent(20400, 1, clock.Now);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
        }
        [Test]
        public void SharedReentrancyGuardCoversClockBeforePreparingState()
        {
            clock.BeforeRead = () =>
            {
                Assert.That(session.State, Is.EqualTo(GameSessionState.Idle));
                Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
                Assert.That(spins.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            };
            Assert.That(daily.Claim().Success, Is.True);
            Consistent(20400, 1, clock.Now);
            clock.BeforeRead = () => Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
            Assert.That(spins.StartSpin().Success, Is.True);
            Assert.That(session.Snapshot.Balance, Is.EqualTo(repository.Load().Data.Balance));
            Assert.That(session.Snapshot.DailyStreakDay, Is.EqualTo(1));
        }
        [Test]
        public void ClaimCannotOverwriteSpinDebitOrSettlementAndSpinPreservesClaim()
        {
            daily.Claim();
            repository.BeforeSave = _ =>
            {
                Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
                Assert.That(daily.RetryPersistence().Error, Is.EqualTo(DailyBonusError.Busy));
            };
            var start = spins.StartSpin();
            Assert.That(start.Success, Is.True);
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
            Assert.That(session.Snapshot.Balance, Is.EqualTo(20400 - session.Snapshot.SelectedBet));
            long debitBalance = session.Snapshot.Balance;
            var complete = spins.CompleteSpin(start.Spin.SpinId);
            Assert.That(complete.Success, Is.True, complete.Diagnostic);
            Consistent(debitBalance + complete.ConfirmedPayout, 1, clock.Now);
            repository.BeforeSave = null;
            clock.Advance(TimeSpan.FromDays(1));
            Assert.That(daily.Claim().Success, Is.True);
            Consistent(debitBalance + complete.ConfirmedPayout + 600, 2, clock.Now);
            Assert.That(session.Snapshot.LastSettledSpinId, Is.EqualTo(start.Spin.SpinId));
        }
        [TestCase(GameSessionState.Preparing)]
        [TestCase(GameSessionState.Spinning)]
        [TestCase(GameSessionState.Settling)]
        [TestCase(GameSessionState.Error)]
        public void EveryNonIdleStateRejectsClaim(GameSessionState target)
        {
            session.Transition(GameSessionState.Preparing);
            if (target == GameSessionState.Spinning || target == GameSessionState.Settling)
                session.Transition(GameSessionState.Spinning);
            if (target == GameSessionState.Settling || target == GameSessionState.Error) session.Transition(target);
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
            Assert.That(repository.SaveAttemptCount, Is.Zero);
            Consistent(20000, 0, null);
        }
        [Test]
        public void PendingSpinAndFailedSpinPersistenceBlockDailyRetryAndClaim()
        {
            repository.FailSaves = true;
            spins.StartSpin();
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
            Assert.That(daily.RetryPersistence().Error, Is.EqualTo(DailyBonusError.InvalidState));
            repository.FailSaves = false;
            Assert.That(spins.RetryPersistence().Success, Is.True);
            Recreate(); // saved PendingSpin awaits startup recovery
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
            Assert.That(spins.RecoverInterruptedSpin().Success, Is.True);
            Assert.That(daily.Claim().Success, Is.True);
            Consistent(20200, 1, clock.Now);
        }
        [Test]
        public void OverflowDoesNotSaveOrChangeConfirmedState()
        {
            var profile = repository.Load().Data; profile.Balance = long.MaxValue;
            repository = new FakeSaveRepository(profile); Recreate();
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.ArithmeticOverflow));
            Assert.That(repository.SaveAttemptCount, Is.Zero);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Idle));
            Consistent(long.MaxValue, 0, null);
        }
        [Test]
        public void NegativeRewardsAreRejectedBeforeServiceConstruction()
        {
            var invalid = WithRules(new DailyBonusRules(true, new long[] { -1, 2, 3, 4, 5, 6, 7 },
                TimeSpan.Zero, StreakResetBehavior.ResetAfterMissedDay));
            Assert.Throws<ConfigValidationException>(() => new DailyBonusService(new GameSession(session.Snapshot),
                invalid, new BalanceService(config), repository, clock));
            Assert.That(repository.SaveAttemptCount, Is.Zero);
            Consistent(20000, 0, null);
        }
        [Test]
        public void ZeroRewardStillRecordsExactlyOneClaim()
        {
            Rules(rewards: new long[7]);
            Assert.That(daily.Claim().Success, Is.True);
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.AlreadyClaimed));
            Consistent(20000, 1, clock.Now);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
        }
        [Test]
        public void DuplicateServiceOwnerCannotLosePendingRetry()
        {
            Assert.Throws<InvalidOperationException>(() => new DailyBonusService(session, config,
                new BalanceService(config), repository, clock));
        }
    }
}
