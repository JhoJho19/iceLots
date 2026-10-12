using System;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using NUnit.Framework;
using UnityEditor;
using SettingsService = IceP.Application.SettingsService;

namespace IceP.Tests
{
    public sealed class SettingsTests
    {
        private SlotRuntimeConfig config;
        private FakeSaveRepository repository;
        private GameSession session;
        private SettingsService settings;
        private DailyBonusService daily;
        private SpinService spins;
        private sealed class FixedRandom : IRandomSource { public int NextInt(int min, int max) => min; }
        [SetUp] public void Setup()
        {
            config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            repository = new FakeSaveRepository(InitialProfileFactory.Create(config)); Recreate();
        }
        private void Recreate()
        {
            session = new GameSession(repository.Load().Data); settings = new SettingsService(session, config, repository);
            var balance = new BalanceService(config);
            daily = new DailyBonusService(session, config, balance, repository, new FakeClock(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero)));
            spins = new SpinService(session, config, new SlotEngine(config, new FixedRandom()), balance, repository,
                () => new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
        }
        [Test] public void DefaultsAndReturnedCopiesAreSafe()
        {
            Assert.That(settings.Current.MusicEnabled && settings.Current.SoundEnabled, Is.True);
            Assert.That(settings.Current.MusicVolume, Is.EqualTo(1)); Assert.That(settings.Current.SoundVolume, Is.EqualTo(1));
            settings.Current.MusicVolume = 0; Assert.That(settings.Current.MusicVolume, Is.EqualTo(1));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void EveryPreferencePersistsAndReloads(int field)
        {
            var value = settings.Current;
            if (field == 0) value.MusicEnabled = false;
            if (field == 1) value.SoundEnabled = false;
            if (field == 2) value.MusicVolume = .35;
            if (field == 3) value.SoundVolume = .65;
            Assert.That(settings.Update(value).Success, Is.True); Recreate();
            Assert.That(settings.Current.MusicEnabled, Is.EqualTo(value.MusicEnabled));
            Assert.That(settings.Current.SoundEnabled, Is.EqualTo(value.SoundEnabled));
            Assert.That(settings.Current.MusicVolume, Is.EqualTo(value.MusicVolume));
            Assert.That(settings.Current.SoundVolume, Is.EqualTo(value.SoundVolume));
        }
        [TestCase(-.1)] [TestCase(1.1)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
        public void RejectsInvalidVolumes(double invalid)
        {
            var value = settings.Current; value.MusicVolume = invalid;
            Assert.That(settings.Update(value).Error, Is.EqualTo(SettingsError.InvalidValue));
            value = settings.Current; value.SoundVolume = invalid;
            Assert.That(settings.Update(value).Error, Is.EqualTo(SettingsError.InvalidValue));
            Assert.That(repository.SaveAttemptCount, Is.Zero); Assert.That(session.State, Is.EqualTo(GameSessionState.Idle));
        }
        [Test] public void NullAndUnsupportedVersionRejected()
        {
            Assert.That(settings.Update(null).Error, Is.EqualTo(SettingsError.InvalidValue));
            var value = settings.Current; value.SettingsVersion = 99;
            Assert.That(settings.Update(value).Error, Is.EqualTo(SettingsError.InvalidValue));
        }
        [Test] public void UnchangedPreferencesDoNotWrite()
        { Assert.That(settings.Update(settings.Current).Success, Is.True); Assert.That(repository.SaveAttemptCount, Is.Zero); }
        [Test] public void StalePreferenceIntentUsesNewestEconomicSnapshot()
        {
            var value = settings.Current; value.MusicVolume = .4;
            Assert.That(daily.Claim().Success, Is.True);
            var start = spins.StartSpin(); Assert.That(start.Success, Is.True);
            Assert.That(settings.Update(value).Error, Is.EqualTo(SettingsError.Busy));
            Assert.That(spins.CompleteSpin(start.Spin.SpinId).Success, Is.True);
            var before = session.Snapshot;
            Assert.That(settings.Update(value).Success, Is.True);
            var after = repository.Load().Data;
            Assert.That(after.Balance, Is.EqualTo(before.Balance)); Assert.That(after.SelectedBet, Is.EqualTo(before.SelectedBet));
            Assert.That(after.DailyLastClaimUtc, Is.EqualTo(before.DailyLastClaimUtc)); Assert.That(after.DailyStreakDay, Is.EqualTo(before.DailyStreakDay));
            Assert.That(after.FreeSpinsRemaining, Is.EqualTo(before.FreeSpinsRemaining)); Assert.That(after.BonusBet, Is.EqualTo(before.BonusBet));
            Assert.That(after.LastSettledSpinId, Is.EqualTo(before.LastSettledSpinId)); Assert.That(after.LastWin, Is.EqualTo(before.LastWin));
            Assert.That(after.JackpotWeekId, Is.EqualTo(before.JackpotWeekId)); Assert.That(after.JackpotWinsThisWeek, Is.EqualTo(before.JackpotWinsThisWeek));
        }
        [TestCase(false)] [TestCase(true)] public void FailureRetainsConfirmedStateAndExactRetry(bool ambiguous)
        {
            repository.CommitThenFail = ambiguous; repository.FailSaves = !ambiguous;
            var value = settings.Current; value.MusicVolume = .25;
            Assert.That(settings.Update(value).Error, Is.EqualTo(SettingsError.PersistenceFailure)); value.MusicVolume = .9;
            Assert.That(settings.Current.MusicVolume, Is.EqualTo(1)); Assert.That(settings.CanRetry, Is.True);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy)); Assert.That(spins.StartSpin().Success, Is.False);
            Assert.That(settings.Update(value).Error, Is.EqualTo(SettingsError.Busy));
            repository.CommitThenFail = repository.FailSaves = false;
            Assert.That(settings.RetryPersistence().Success, Is.True);
            Assert.That(settings.Current.MusicVolume, Is.EqualTo(.25)); Assert.That(repository.Load().Data.Balance, Is.EqualTo(20000));
            Assert.That(settings.RetryPersistence().Error, Is.EqualTo(SettingsError.InvalidState)); Recreate();
            Assert.That(settings.Current.MusicVolume, Is.EqualTo(.25));
        }
        [Test] public void ConfirmedJackpotSettlementSurvivesLaterPreferencesWrite()
        {
            var shared = new GameSession(InitialProfileFactory.Create(config));
            var clock = new FakeClock(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
            var jackpot = new WeeklyJackpotService(shared, config, clock, new FixedRandom());
            var spin = new SpinService(shared, config, new SlotEngine(config, new FixedRandom()), new BalanceService(config), repository, () => clock.UtcNow, jackpot);
            var prefs = new SettingsService(shared, config, repository);
            var intent = prefs.Current; intent.SoundVolume = .5;
            Assert.That(spin.SelectBet(1000).Success, Is.True);
            var start = spin.StartSpin(); Assert.That(start.Spin.Jackpot.JackpotWon, Is.True);
            Assert.That(spin.CompleteSpin(start.Spin.SpinId).Success, Is.True);
            var confirmed = shared.Snapshot;
            Assert.That(prefs.Update(intent).Success, Is.True);
            var saved = repository.Load().Data;
            Assert.That(saved.Balance, Is.EqualTo(confirmed.Balance)); Assert.That(saved.LastWin, Is.EqualTo(confirmed.LastWin));
            Assert.That(saved.JackpotWeekId, Is.EqualTo(confirmed.JackpotWeekId)); Assert.That(saved.JackpotWinsThisWeek, Is.EqualTo(1));
            Assert.That(saved.JackpotEventId, Is.EqualTo(confirmed.JackpotEventId)); Assert.That(saved.LastSettledSpinId, Is.EqualTo(start.Spin.SpinId));
        }
        [Test] public void ReentrantWritesAndNavigationAreRejected()
        {
            var nav = new GameNavigationService(session);
            repository.BeforeSave = _ =>
            {
                Assert.That(settings.Update(settings.Current).Error, Is.EqualTo(SettingsError.Busy));
                Assert.That(settings.RetryPersistence().Error, Is.EqualTo(SettingsError.Busy));
                Assert.That(daily.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
                nav.NavigateToSlot(); Assert.That(nav.CurrentScreen, Is.EqualTo(GameScreen.MainMenu));
            };
            var value = settings.Current; value.SoundEnabled = false; Assert.That(settings.Update(value).Success, Is.True);
        }
        [Test] public void AnotherServiceErrorCannotBeBypassed()
        {
            repository.FailSaves = true; daily.Claim();
            Assert.That(settings.CanRetry || settings.CanUpdate, Is.False);
            Assert.That(settings.Update(settings.Current).Error, Is.EqualTo(SettingsError.Busy));
            repository.FailSaves = false; Assert.That(daily.RetryPersistence().Success, Is.True);
            Assert.That(settings.CanUpdate, Is.True);
        }
        [Test] public void RepositoryExceptionKeepsExactRetry()
        {
            repository.BeforeSave = _ => throw new InvalidOperationException("injected");
            var value = settings.Current; value.SoundVolume = .2;
            Assert.That(settings.Update(value).Error, Is.EqualTo(SettingsError.PersistenceFailure));
            repository.BeforeSave = null; Assert.That(settings.RetryPersistence().Success, Is.True);
            Assert.That(settings.Current.SoundVolume, Is.EqualTo(.2));
        }
        [Test] public void DuplicateOwnerRejected() => Assert.Throws<InvalidOperationException>(() => new SettingsService(session, config, repository));
    }
}
