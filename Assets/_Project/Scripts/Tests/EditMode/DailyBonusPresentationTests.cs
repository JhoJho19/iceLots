using System;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Infrastructure.Configuration;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor;

namespace IceP.Tests
{
    public sealed class DailyBonusPresentationTests
    {
        private SlotRuntimeConfig config;
        private FakeClock clock;
        private FakeSaveRepository repository;
        private GameSession session;
        private DailyBonusService daily;
        private DailyBonusDisplay Display => new DailyBonusDisplay(daily.GetStatus());
        [SetUp] public void Setup()
        {
            config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            clock = new FakeClock(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
            repository = new FakeSaveRepository(InitialProfileFactory.Create(config)); Recreate();
        }
        private void Recreate()
        { session = new GameSession(repository.Load().Data); daily = new DailyBonusService(session, config, new BalanceService(config), repository, clock); }
        [Test] public void OpeningProjectionDoesNotClaimAndMatchesService()
        {
            var display = Display;
            Assert.That(display.Status.CanClaim, Is.True); Assert.That(display.Days[0], Is.EqualTo(RewardDisplayState.Available));
            Assert.That(display.Status.Rewards, Is.EqualTo(config.DailyBonus.Rewards)); Assert.That(repository.SaveAttemptCount, Is.Zero);
        }
        [Test] public void ClaimUpdatesConfirmedProgressAndDuplicateDoesNotReward()
        {
            var result = daily.Claim(); Assert.That(result.Success, Is.True); Assert.That(result.ConfirmedBalance, Is.EqualTo(20400));
            Assert.That(Display.Days[0], Is.EqualTo(RewardDisplayState.Claimed)); Assert.That(Display.Days[1], Is.EqualTo(RewardDisplayState.Current));
            Assert.That(Display.Status.CanClaim, Is.False); Assert.That(Display.Status.NextAvailableUtc, Is.Not.Null);
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.AlreadyClaimed)); Assert.That(session.Snapshot.Balance, Is.EqualTo(20400));
        }
        [Test] public void SeventhDayWrapShowsCompletedCycleThenFreshDayOne()
        {
            for (int i = 0; i < 7; i++)
            {
                Assert.That(Display.Status.RewardDay, Is.EqualTo(i + 1)); Assert.That(daily.Claim().Success, Is.True);
                if (i < 6) clock.Now = clock.Now.AddDays(1);
            }
            Assert.That(Display.Days, Is.All.EqualTo(RewardDisplayState.Claimed));
            Assert.That(Display.Status.CanClaim, Is.False); clock.Now = clock.Now.AddDays(1);
            Assert.That(Display.Days[0], Is.EqualTo(RewardDisplayState.Available));
            Assert.That(Display.Days[1], Is.EqualTo(RewardDisplayState.Future));
        }
        [Test] public void MissedPeriodClearsDisplayedProgress()
        {
            daily.Claim(); clock.Now = clock.Now.AddDays(1); daily.Claim(); clock.Now = clock.Now.AddDays(2);
            Assert.That(Display.Days[0], Is.EqualTo(RewardDisplayState.Available)); Assert.That(Display.Days[1], Is.EqualTo(RewardDisplayState.Future));
        }
        [TestCase(false)] [TestCase(true)] public void UnconfirmedClaimNeverShowsClaimedOrSuccess(bool ambiguous)
        {
            repository.CommitThenFail = ambiguous; repository.FailSaves = !ambiguous;
            Assert.That(daily.Claim().Success, Is.False); Assert.That(Display.Status.CanClaim, Is.False);
            Assert.That(Display.Days[0], Is.EqualTo(RewardDisplayState.Current)); Assert.That(session.Snapshot.Balance, Is.EqualTo(20000));
            repository.CommitThenFail = repository.FailSaves = false;
            Assert.That(daily.RetryPersistence().Success, Is.True); Assert.That(Display.Days[0], Is.EqualTo(RewardDisplayState.Claimed));
        }
        [TestCase(true)] [TestCase(false)] public void DisabledAndCustomConfiguredRewardsUseStatus(bool enabled)
        {
            var rules = new DailyBonusRules(enabled, new long[] { 1, 2, 3, 4, 5, 6, 7 }, TimeSpan.Zero, StreakResetBehavior.ResetAfterMissedDay);
            config = new SlotRuntimeConfig(config.ConfigVersion, config.ReelCount, config.VisibleRows, config.ScatterId,
                config.Symbols, config.ReelStrips, config.Paylines, config.Paytable, config.Economy, config.Scatter, rules, config.Jackpot, config.DebugRandom);
            Recreate(); Assert.That(Display.Status.CanClaim, Is.EqualTo(enabled)); Assert.That(Display.Status.RewardAmount, Is.EqualTo(1));
            Assert.That(Display.Status.Rewards, Is.EqualTo(rules.Rewards));
        }
        [Test] public void NewPeriodRefreshUsesApplicationClock()
        {
            clock.Now = new DateTimeOffset(2026, 10, 12, 23, 59, 59, TimeSpan.Zero); daily.Claim();
            Assert.That(Display.Status.CanClaim, Is.False); clock.Now = clock.Now.AddSeconds(1);
            Assert.That(Display.Days[1], Is.EqualTo(RewardDisplayState.Available)); Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
        }
    }
}
