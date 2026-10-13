#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Bootstrap;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IceP.Tests
{
    public sealed class JackpotHudTests
    {
        private string directory, previous;
        private SlotBootstrapper boot;
        private SlotView slotView;
        private JackpotView hud;
        private JackpotPresenter presenter;
        private SlotPresenter slot;
        private GameSession session;
        private SpinService spins;
        private WeeklyJackpotService jackpot;
        private Clock clock;
        private MemorySave save;
        private Bits bits;
        private sealed class Clock : IClock { public DateTimeOffset Now = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero); public DateTimeOffset UtcNow => Now; }
        private sealed class Bits : IRandomSource { public int Calls; public int NextInt(int min, int max) { Calls++; return min; } }
        private sealed class MemorySave : ISaveRepository
        {
            public SaveData Data; public bool Fail; public int Writes;
            public SaveLoadResult Load() => new SaveLoadResult(SaveLoadStatus.Loaded, Data);
            public SaveWriteResult Save(SaveData value) { Writes++; if (Fail) return new SaveWriteResult(PersistenceError.IoFailure, "Injected failure"); Data = value.Copy(); return new SaveWriteResult(); }
            public SaveWriteResult Reset(SaveData value) => Save(value);
        }
        [SetUp] public void Setup()
        {
            previous = Environment.GetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY");
            directory = Path.Combine(Path.GetTempPath(), "IceP-JackpotHUD-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", directory);
        }
        [TearDown] public void Cleanup()
        {
            Time.timeScale = 1; presenter?.Dispose(); slot?.Dispose();
            Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", previous);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private IEnumerator Load()
        {
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/SC.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            slotView = Object.FindFirstObjectByType<SlotView>(FindObjectsInactive.Include);
            hud = Object.FindFirstObjectByType<JackpotView>(FindObjectsInactive.Include);
            Assert.That(boot.IsInitialized, Is.True); Assert.That(hud.HasRequiredReferences, Is.True);
            Object.FindFirstObjectByType<GameNavigationView>().PlayButton.onClick.Invoke();
        }
        private void Control(int free = 0, bool enabled = true, long amount = 100000, DateTimeOffset? start = null)
        {
            boot.JackpotPresenter.Dispose(); boot.SlotPresenter.Dispose(); boot.enabled = false;
            var c = boot.RuntimeConfig; var j = c.Jackpot;
            var config = new SlotRuntimeConfig(c.ConfigVersion, c.ReelCount, c.VisibleRows, c.ScatterId, c.Symbols,
                c.ReelStrips, c.Paylines, c.Paytable, c.Economy, c.Scatter, c.DailyBonus,
                new JackpotRulesConfig(enabled, j.EventId, j.ConfigVersion, amount, j.BaseProbability, j.ReferenceBet,
                    j.MaxWinsPerWeek, j.ResetWeekday, j.ResetTimeUtc, start, j.EndUtc), c.DebugRandom);
            var data = InitialProfileFactory.Create(config); data.SelectedBet = 1000;
            data.FreeSpinsRemaining = free; data.BonusBet = free > 0 ? 1000 : 0;
            session = new GameSession(data); save = new MemorySave { Data = data }; clock = new Clock(); bits = new Bits();
            jackpot = new WeeklyJackpotService(session, config, clock, bits);
            spins = new SpinService(session, config, new SlotEngine(config, new Bits()),
                new BalanceService(config), save, () => clock.UtcNow, jackpot);
            var navigation = new GameNavigationService(session); navigation.NavigateToSlot();
            slot = new SlotPresenter(session, spins, navigation, boot.RuntimeConfig.Economy.AvailableBets, slotView);
            presenter = new JackpotPresenter(jackpot, clock, hud, slot, navigation);
        }
        private IEnumerator Finish()
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (slotView.AnimationCoordinator.IsRunning && Time.realtimeSinceStartup < deadline)
            { Assert.That(hud.gameObject.activeInHierarchy, Is.True); yield return null; }
            Assert.That(slotView.AnimationCoordinator.IsRunning, Is.False);
        }
        [UnityTest] public IEnumerator ExistingSceneContainsVisibleConfiguredHudAndReopensOnce()
        {
            yield return Load();
            Assert.That(hud.transform.parent, Is.SameAs(slotView.transform)); Assert.That(hud.gameObject.name, Is.EqualTo("JackpotHUD"));
            Assert.That(hud.AmountText, Is.EqualTo("100,000 COINS")); Assert.That(hud.StatusText, Is.EqualTo("ACTIVE"));
            Assert.That(hud.ResetText, Does.StartWith("Next reset in"));
            var nav = Object.FindFirstObjectByType<GameNavigationView>(); var jp = boot.JackpotPresenter;
            for (int i = 0; i < 5; i++) { slotView.BackButton.onClick.Invoke(); Assert.That(hud.gameObject.activeInHierarchy, Is.False); nav.PlayButton.onClick.Invoke(); Assert.That(hud.gameObject.activeInHierarchy, Is.True); }
            Assert.That(boot.JackpotPresenter, Is.SameAs(jp)); Assert.That(Object.FindObjectsByType<JackpotView>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator DeterministicPaidSpinConfirmsAndCelebratesExactlyOnce()
        {
            yield return Load(); Control(); slotView.SpinButton.onClick.Invoke(); var spin = slot.ActiveSpin;
            Assert.That(spin.Jackpot.JackpotWon, Is.True); Assert.That(hud.StatusText, Is.EqualTo("ACTIVE")); Assert.That(hud.CelebrationCount, Is.Zero);
            Assert.That(slotView.BackButton.interactable, Is.False); slotView.BackButton.onClick.Invoke(); Assert.That(hud.gameObject.activeInHierarchy, Is.True);
            yield return Finish();
            Assert.That(session.State, Is.EqualTo(GameSessionState.Idle)); Assert.That(hud.StatusText, Is.EqualTo("WON THIS WEEK"));
            Assert.That(hud.CelebrationCount, Is.EqualTo(1)); Assert.That(hud.IsCelebrating, Is.True);
            presenter.OnSettlement(spin, spins.CompleteSpin(spin.SpinId)); Assert.That(hud.CelebrationCount, Is.EqualTo(1));
            Assert.That(session.Snapshot.Balance, Is.EqualTo(19000 + spin.PotentialPayout));
            var draws = bits.Calls; slotView.SpinButton.onClick.Invoke(); Assert.That(slot.ActiveSpin.Jackpot.Participated, Is.False);
            yield return Finish(); Assert.That(bits.Calls, Is.EqualTo(draws)); Assert.That(hud.CelebrationCount, Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(2.1f); Assert.That(hud.IsCelebrating, Is.False); Assert.That(hud.gameObject.activeInHierarchy, Is.True);
        }
        [UnityTest] public IEnumerator FailedSettlementRetainsConfirmedStateUntilRetry()
        {
            yield return Load(); Control(); slotView.SpinButton.onClick.Invoke(); var spin = slot.ActiveSpin; var draws = bits.Calls;
            save.Fail = true; yield return Finish(); presenter.Tick(1);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error)); Assert.That(hud.StatusText, Is.EqualTo("ACTIVE")); Assert.That(hud.CelebrationCount, Is.Zero);
            Assert.That(session.Snapshot.Balance, Is.EqualTo(19000)); Assert.That(slotView.BackButton.interactable, Is.False);
            slotView.RetryButton.onClick.Invoke(); Assert.That(hud.CelebrationCount, Is.Zero);
            save.Fail = false; slotView.RetryButton.onClick.Invoke(); Assert.That(hud.StatusText, Is.EqualTo("WON THIS WEEK"));
            Assert.That(hud.CelebrationCount, Is.EqualTo(1)); Assert.That(bits.Calls, Is.EqualTo(draws)); Assert.That(slotView.AnimationCoordinator.IsRunning, Is.False);
            presenter.OnSettlement(spin, spins.CompleteSpin(spin.SpinId)); Assert.That(hud.CelebrationCount, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator FreeSpinKeepsHudWithoutJackpotDraw()
        {
            yield return Load(); Control(2); slotView.SpinButton.onClick.Invoke(); var spin = slot.ActiveSpin;
            Assert.That(spin.Type, Is.EqualTo(PendingSpinType.Free)); Assert.That(spin.Jackpot.Participated, Is.False);
            yield return Finish(); Assert.That(hud.StatusText, Is.EqualTo("ACTIVE")); Assert.That(hud.CelebrationCount, Is.Zero);
            Assert.That(bits.Calls, Is.Zero); Assert.That(session.Snapshot.FreeSpinsRemaining, Is.EqualTo(1 + spin.Evaluation.Scatter.AwardedFreeSpins));
        }
        [UnityTest] public IEnumerator ExactWeekBoundaryRestoresEligibilityAndRetainsSavedWeek()
        {
            yield return Load(); Control(); slotView.SpinButton.onClick.Invoke(); yield return Finish();
            var savedWeek = session.Snapshot.JackpotWeekId; var writes = save.Writes;
            clock.Now = jackpot.GetStatus().NextResetUtc.Value.AddTicks(-1); presenter.Tick(1); Assert.That(hud.StatusText, Is.EqualTo("WON THIS WEEK"));
            clock.Now = clock.Now.AddTicks(1); presenter.Tick(1); Assert.That(hud.StatusText, Is.EqualTo("ACTIVE"));
            Assert.That(session.Snapshot.JackpotWeekId, Is.EqualTo(savedWeek)); Assert.That(save.Writes, Is.EqualTo(writes));
        }
        [UnityTest] public IEnumerator SpinAcrossBoundaryCelebratesAcceptedWeekAndShowsCurrentWeek()
        {
            yield return Load(); Control(); clock.Now = jackpot.GetStatus().NextResetUtc.Value.AddSeconds(-2);
            slotView.SpinButton.onClick.Invoke(); var week = slot.ActiveSpin.JackpotWeekId;
            clock.Now = clock.Now.AddSeconds(4); presenter.Tick(1); yield return Finish();
            Assert.That(session.Snapshot.JackpotWeekId, Is.EqualTo(week)); Assert.That(hud.StatusText, Is.EqualTo("ACTIVE")); Assert.That(hud.CelebrationCount, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator CountdownUsesUnscaledRefreshAndFocusResume()
        {
            yield return Load(); Control(); var before = hud.ResetText; Time.timeScale = 0;
            clock.Now = clock.Now.AddSeconds(2); presenter.Tick(1); yield return null;
            Assert.That(hud.ResetText, Is.Not.EqualTo(before)); Assert.That(save.Writes, Is.Zero); Assert.That(bits.Calls, Is.Zero);
            Time.timeScale = 1;
            Assert.That(hud.gameObject.activeInHierarchy, Is.True);
        }
        [UnityTest] public IEnumerator ProductionUnscaledLoopAndResumeRefreshHud()
        {
            yield return Load(); var before = hud.ResetText; Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(2.1f);
            Assert.That(hud.ResetText, Is.Not.EqualTo(before));
            boot.SendMessage("OnApplicationFocus", true); boot.SendMessage("OnApplicationPause", false);
            Assert.That(hud.ResetText, Does.StartWith("Next reset in"));
            Assert.That(hud.gameObject.activeInHierarchy, Is.True);
        }
        [UnityTest] public IEnumerator DestroyHudDuringCelebrationCancelsWithoutEconomicChanges()
        {
            yield return Load(); Control(); slotView.SpinButton.onClick.Invoke(); yield return Finish();
            var balance = session.Snapshot.Balance; var writes = save.Writes;
            Object.Destroy(hud.gameObject); yield return null; presenter.Tick(1); yield return new WaitForSecondsRealtime(2.1f);
            Assert.That(session.Snapshot.Balance, Is.EqualTo(balance)); Assert.That(save.Writes, Is.EqualTo(writes)); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator SceneReloadCancelsCallbacksAndUsesOneHud()
        {
            yield return Load(); slotView.SpinButton.onClick.Invoke(); var accepted = boot.Session.Snapshot.Balance;
            yield return Load();
            Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(accepted)); Assert.That(boot.Session.Snapshot.LastWin, Is.Zero);
            Assert.That(Object.FindObjectsByType<JackpotView>(FindObjectsSortMode.None).Length, Is.EqualTo(1)); Assert.That(hud.CelebrationCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator DisabledEventRemainsVisibleWithConfiguredAmount()
        {
            yield return Load(); Control(enabled: false, amount: 234567);
            Assert.That(hud.gameObject.activeInHierarchy, Is.True); Assert.That(hud.AmountText, Is.EqualTo("234,567 COINS"));
            Assert.That(hud.StatusText, Is.EqualTo("INACTIVE")); Assert.That(hud.AvailabilityText, Is.EqualTo("Event disabled"));
            slotView.SpinButton.onClick.Invoke(); yield return Finish(); Assert.That(bits.Calls, Is.Zero); Assert.That(hud.CelebrationCount, Is.Zero);
        }
        [UnityTest] public IEnumerator FutureStartExplainsAvailability()
        {
            yield return Load(); Control(start: new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
            Assert.That(hud.StatusText, Is.EqualTo("INACTIVE")); Assert.That(hud.AvailabilityText, Does.Contain("14 Oct 2026 12:00 UTC"));
            clock.Now = clock.Now.AddDays(2); presenter.Tick(1); Assert.That(hud.StatusText, Is.EqualTo("ACTIVE"));
        }
        [UnityTest] public IEnumerator LargeAmountFitsSingleLineAndHudAvoidsGameplay()
        {
            yield return Load(); Control(amount: 9000000000000000); yield return null; Canvas.ForceUpdateCanvases();
            var label = hud.transform.Find("Amount").GetComponent<UnityEngine.UI.Text>();
            Assert.That(hud.AmountText, Is.EqualTo("9,000,000,000,000,000 COINS")); Assert.That(label.cachedTextGenerator.lineCount, Is.EqualTo(1));
            Assert.That(label.cachedTextGenerator.characterCountVisible, Is.GreaterThanOrEqualTo(hud.AmountText.Length));
            var h = Bounds((RectTransform)hud.transform);
            foreach (var r in new[] { (RectTransform)slotView.AnimationCoordinator.transform, (RectTransform)slotView.SpinButton.transform,
                (RectTransform)slotView.BackButton.transform, (RectTransform)slotView.transform.Find("SlotHUD/Balance"),
                (RectTransform)slotView.transform.Find("SlotHUD/LastWin"), (RectTransform)slotView.transform.Find("SlotHUD/FreeSpins"), (RectTransform)slotView.transform.Find("SlotControls") })
                Assert.That(h.Overlaps(Bounds(r)), Is.False, r.name);
            var canvas = Bounds(hud.GetComponentInParent<Canvas>().GetComponent<RectTransform>());
            Assert.That(canvas.Contains(h.min) && canvas.Contains(h.max), Is.True);
        }
        private static Rect Bounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
#endif
