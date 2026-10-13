using System;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IceP.Tests
{
    public sealed class JackpotPresentationTests
    {
        private SlotRuntimeConfig config;
        private FakeClock clock;
        private FakeSaveRepository save;
        private GameSession session;
        private WeeklyJackpotService jackpot;
        private SpinService spins;
        private GameNavigationService navigation;
        private SlotPresenter slot;
        private JackpotPresenter presenter;
        private Hud hud;
        private SlotFake view;
        private Bits bits;
        private sealed class Bits : IRandomSource
        { public int Calls; public int NextInt(int min, int max) { Calls++; return min; } }
        private sealed class Hud : IJackpotView
        {
            public event Action Activated;
            public event Action Destroyed;
            public JackpotDisplay Display; public int Celebrations, Renders, Cancels;
            public void Render(JackpotDisplay display) { Display = display; Renders++; }
            public void Celebrate() => Celebrations++;
            public void CancelCelebration() => Cancels++;
            public void Open() => Activated?.Invoke();
            public void Destroy() => Destroyed?.Invoke();
        }
        private sealed class SlotFake : ISlotView
        {
            public event Action SpinRequested;
            public event Action<int> BetStepRequested { add { } remove { } }
            public event Action BackRequested { add { } remove { } }
            public event Action RetryRequested;
            public event Action<Guid, bool> AnimationFinished;
            public event Action Destroyed { add { } remove { } }
            public SpinResult Spin;
            public void Start() => SpinRequested?.Invoke();
            public void Finish() => AnimationFinished?.Invoke(Spin.SpinId, true);
            public void Retry() => RetryRequested?.Invoke();
            public void Render(SlotDisplay display) { }
            public void ShowMessage(string text) { }
            public void ClearWins() { }
            public void Animate(SpinResult spin) => Spin = spin;
            public void ShowConfirmedWin(SpinResult spin) { }
            public void CancelAnimation() { }
        }
        [SetUp] public void Setup()
        {
            config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            clock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
            Create();
        }
        private void Create(SaveData profile = null)
        {
            presenter?.Dispose(); slot?.Dispose();
            profile = profile ?? InitialProfileFactory.Create(config); profile.SelectedBet = 1000;
            save = new FakeSaveRepository(profile); session = new GameSession(profile); bits = new Bits();
            jackpot = new WeeklyJackpotService(session, config, clock, bits);
            spins = new SpinService(session, config, new SlotEngine(config, new Bits()), new BalanceService(config), save, () => clock.UtcNow, jackpot);
            navigation = new GameNavigationService(session); navigation.NavigateToSlot();
            view = new SlotFake(); hud = new Hud();
            slot = new SlotPresenter(session, spins, navigation, config.Economy.AvailableBets, view);
            presenter = new JackpotPresenter(jackpot, clock, hud, slot, navigation);
        }
        private void Rules(bool enabled = true, long amount = 100000, DateTimeOffset? start = null,
            DayOfWeek weekday = DayOfWeek.Monday, TimeSpan? time = null)
        {
            var c = config; var j = c.Jackpot;
            config = new SlotRuntimeConfig(c.ConfigVersion, c.ReelCount, c.VisibleRows, c.ScatterId, c.Symbols,
                c.ReelStrips, c.Paylines, c.Paytable, c.Economy, c.Scatter, c.DailyBonus,
                new JackpotRulesConfig(enabled, j.EventId, j.ConfigVersion, amount, j.BaseProbability, j.ReferenceBet,
                    j.MaxWinsPerWeek, weekday, time ?? TimeSpan.Zero, start, null), c.DebugRandom);
            Create();
        }
        [TearDown] public void Cleanup() { presenter.Dispose(); slot.Dispose(); }
        [Test] public void ConfiguredAmountAndActiveMap() { Assert.That(hud.Display.Amount, Is.EqualTo("100,000 COINS")); Assert.That(hud.Display.Status, Is.EqualTo("ACTIVE")); }
        [Test] public void CustomAmountMaps() { Rules(amount: 234567); Assert.That(hud.Display.Amount, Is.EqualTo("234,567 COINS")); }
        [Test] public void LargeAmountFormatsWithoutOverflow() { Rules(amount: 9000000000000000); Assert.That(hud.Display.Amount, Is.EqualTo("9,000,000,000,000,000 COINS")); }
        [Test] public void DisabledStillRepresented() { Rules(enabled: false); Assert.That(hud.Display.Status, Is.EqualTo("INACTIVE")); Assert.That(hud.Display.Amount, Is.EqualTo("100,000 COINS")); Assert.That(hud.Display.Availability, Is.EqualTo("Event disabled")); }
        [Test] public void FutureStartIsMeaningful() { Rules(start: clock.Now.AddDays(2)); Assert.That(hud.Display.Status, Is.EqualTo("INACTIVE")); Assert.That(hud.Display.Availability, Does.Contain("14 Oct 2026 12:00 UTC")); }
        [Test] public void CountdownFormats() { Assert.That(JackpotDisplay.FormatReset(clock.Now.Add(new TimeSpan(2, 14, 32, 9)), clock.Now), Is.EqualTo("Next reset in 02d 14h 32m 09s")); }
        [Test] public void NullResetSafe() { Assert.That(JackpotDisplay.FormatReset(null, clock.Now), Is.EqualTo("Next reset unavailable")); }
        [Test] public void ExactCountdownBoundary() { Assert.That(JackpotDisplay.FormatReset(clock.Now, clock.Now), Is.EqualTo("Reset due")); }
        [Test] public void PastCountdownClamped() { Assert.That(JackpotDisplay.FormatReset(clock.Now.AddSeconds(-1), clock.Now), Is.EqualTo("Reset due")); }
        [Test] public void PotentialWinDoesNotCelebrate() { view.Start(); Assert.That(view.Spin.Jackpot.JackpotWon, Is.True); presenter.Refresh(); Assert.That(hud.Celebrations, Is.Zero); Assert.That(hud.Display.Status, Is.EqualTo("ACTIVE")); }
        [Test] public void FailedSettlementAndRetryConfirmOnce()
        {
            view.Start(); save.FailSaves = true; view.Finish(); presenter.Tick(1);
            Assert.That(hud.Celebrations, Is.Zero); Assert.That(hud.Display.Status, Is.EqualTo("ACTIVE"));
            save.FailSaves = false; view.Retry(); Assert.That(hud.Celebrations, Is.EqualTo(1));
            Assert.That(hud.Display.Status, Is.EqualTo("WON THIS WEEK")); view.Retry(); view.Finish(); Assert.That(hud.Celebrations, Is.EqualTo(1));
        }
        [Test] public void SuccessfulSettlementAndDuplicateNotification()
        {
            view.Start(); var spin = view.Spin; view.Finish();
            presenter.OnSettlement(spin, spins.CompleteSpin(spin.SpinId));
            Assert.That(hud.Celebrations, Is.EqualTo(1)); Assert.That(hud.Display.Status, Is.EqualTo("WON THIS WEEK")); Assert.That(hud.Display.Amount, Is.EqualTo("100,000 COINS"));
        }
        [Test] public void AcceptanceIsNotSettlementConfirmation()
        { var start = spins.StartSpin(); presenter.OnSettlement(start.Spin, start); Assert.That(hud.Celebrations, Is.Zero); }
        [Test] public void RefreshDoesNotWriteMutateOrDraw()
        {
            var before = session.Snapshot;
            for (int i = 0; i < 20; i++) presenter.Tick(1);
            Assert.That(save.SaveAttemptCount, Is.Zero); Assert.That(bits.Calls, Is.Zero);
            Assert.That(session.Snapshot.JackpotWeekId, Is.EqualTo(before.JackpotWeekId)); Assert.That(session.Snapshot.Balance, Is.EqualTo(before.Balance));
        }
        [Test] public void TickIsThrottled() { var count = hud.Renders; for (int i = 0; i < 9; i++) presenter.Tick(.1f); Assert.That(hud.Renders, Is.EqualTo(count)); presenter.Tick(.2f); Assert.That(hud.Renders, Is.EqualTo(count + 1)); }
        [Test] public void FreeSpinCannotParticipateOrCelebrate()
        {
            var data = InitialProfileFactory.Create(config); data.FreeSpinsRemaining = 1; data.BonusBet = 1000; Create(data);
            view.Start(); Assert.That(view.Spin.Jackpot.Participated, Is.False); view.Finish();
            Assert.That(bits.Calls, Is.Zero); Assert.That(hud.Celebrations, Is.Zero);
        }
        [Test] public void WeekResetChangesWonToActiveWithoutWriting()
        {
            view.Start(); view.Finish(); clock.Now = jackpot.GetStatus().NextResetUtc.Value;
            int writes = save.SaveAttemptCount; presenter.Tick(1);
            Assert.That(hud.Display.Status, Is.EqualTo("ACTIVE")); Assert.That(save.SaveAttemptCount, Is.EqualTo(writes));
        }
        [Test] public void CrossingWeekCelebratesOldWinAndDisplaysCurrentEligibility()
        {
            clock.Now = jackpot.GetStatus().NextResetUtc.Value.AddSeconds(-2); view.Start(); var old = view.Spin.JackpotWeekId;
            clock.Advance(TimeSpan.FromSeconds(4)); view.Finish();
            Assert.That(session.Snapshot.JackpotWeekId, Is.EqualTo(old)); Assert.That(hud.Display.Status, Is.EqualTo("ACTIVE")); Assert.That(hud.Celebrations, Is.EqualTo(1));
        }
        [Test] public void ConfiguredResetScheduleUsesApplicationTimestamp()
        { Rules(weekday: DayOfWeek.Wednesday, time: TimeSpan.FromHours(6)); Assert.That(hud.Display.Reset, Is.EqualTo(JackpotDisplay.FormatReset(jackpot.GetStatus().NextResetUtc, clock.Now))); }
        [Test] public void ReactivationRefreshesImmediately() { clock.Advance(TimeSpan.FromSeconds(8)); var count = hud.Renders; hud.Open(); Assert.That(hud.Renders, Is.EqualTo(count + 1)); }
        [Test] public void DestroyedViewDetachesAndCancels()
        { view.Start(); hud.Destroy(); var count = hud.Renders; view.Finish(); presenter.Tick(1); hud.Open(); Assert.That(hud.Renders, Is.EqualTo(count)); Assert.That(hud.Celebrations, Is.Zero); Assert.That(hud.Cancels, Is.EqualTo(1)); }
        [Test] public void ReopeningReusesPresenterAndOneSubscription()
        { for (int i = 0; i < 5; i++) { navigation.NavigateToMainMenu(); navigation.NavigateToSlot(); } view.Start(); view.Finish(); Assert.That(hud.Celebrations, Is.EqualTo(1)); }
    }
}
