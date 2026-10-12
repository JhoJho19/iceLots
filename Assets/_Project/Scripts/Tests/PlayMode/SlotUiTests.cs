#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Bootstrap;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using IceP.Infrastructure.Persistence;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IceP.Tests
{
    public sealed class SlotUiTests
    {
        private string directory, previous;
        private SlotBootstrapper boot;
        private GameNavigationView nav;
        private SlotView view;
        private SlotPresenter controlled;
        private GameSession controlledSession;
        private MemorySave controlledSave;
        private sealed class Stops : IRandomSource
        {
            public int[] Positions;
            public int Calls;
            public int NextInt(int min, int max) => Positions[Calls++ % 5];
        }
        private sealed class MemorySave : ISaveRepository
        {
            public SaveData Data;
            public int Writes;
            public bool Fail;
            public SaveLoadResult Load() => new SaveLoadResult(SaveLoadStatus.Loaded, Data);
            public SaveWriteResult Save(SaveData data)
            {
                Writes++; if (Fail) return new SaveWriteResult(PersistenceError.IoFailure, "Injected failure");
                Data = data.Copy(); return new SaveWriteResult();
            }
            public SaveWriteResult Reset(SaveData data) => Save(data);
        }
        [SetUp] public void Setup()
        {
            previous = Environment.GetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY");
            directory = Path.Combine(Path.GetTempPath(), "IceP-SlotUI-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", directory);
        }
        [TearDown] public void Cleanup()
        {
            Time.timeScale = 1;
            controlled?.Dispose(); Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", previous);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private IEnumerator Load(bool open = true)
        {
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/SC.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            boot = Object.FindFirstObjectByType<SlotBootstrapper>(); nav = Object.FindFirstObjectByType<GameNavigationView>();
            view = Object.FindFirstObjectByType<SlotView>(FindObjectsInactive.Include);
            Assert.That(boot.IsInitialized && view.HasRequiredReferences, Is.True);
            if (open) nav.PlayButton.onClick.Invoke();
        }
        private IEnumerator Finish(GameSession session)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (session.State == GameSessionState.Spinning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(view.AnimationCoordinator.IsRunning, Is.False);
        }
        // Deterministic Application fixture injected into the actual authored scene view.
        private Stops Control(int[] positions, int free = 0, long bonusBet = 600)
        {
            boot.SlotPresenter.Dispose(); boot.enabled = false;
            var profile = InitialProfileFactory.Create(boot.RuntimeConfig); profile.FreeSpinsRemaining = free; profile.BonusBet = free > 0 ? bonusBet : 0;
            controlledSave = new MemorySave { Data = profile }; controlledSession = new GameSession(profile);
            var random = new Stops { Positions = positions };
            var spins = new SpinService(controlledSession, boot.RuntimeConfig, new SlotEngine(boot.RuntimeConfig, random),
                new BalanceService(boot.RuntimeConfig), controlledSave, () => new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
            var navigation = new GameNavigationService(controlledSession); navigation.NavigateToSlot();
            controlled = new SlotPresenter(controlledSession, spins, navigation, boot.RuntimeConfig.Economy.AvailableBets, view);
            return random;
        }
        [UnityTest] public IEnumerator PlayOpensExistingScreenAndAllEightMappingsRender()
        {
            yield return Load(false); var handle = SceneManager.GetActiveScene().handle;
            Assert.That(view.gameObject.activeSelf, Is.False); nav.PlayButton.onClick.Invoke();
            Assert.That(view.gameObject.name, Is.EqualTo("GameScreen")); Assert.That(view.gameObject.activeSelf, Is.True);
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(handle)); Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
            Assert.That(view.AnimationCoordinator.Reels.Length, Is.EqualTo(5));
            Assert.That(view.Paylines.GetComponent<CanvasRenderer>(), Is.Not.Null);
            var visual = AssetDatabase.LoadAssetAtPath<SlotVisualConfigSO>("Assets/_Project/Configs/SlotVisualConfigSO.asset");
            foreach (var mapping in visual.Symbols)
            {
                for (int reel = 0; reel < 5; reel++)
                {
                    int stop = Enumerable.Range(0, 30).First(i => boot.RuntimeConfig.ReelStrips[reel].Symbols[i] == mapping.Symbol);
                    var rv = view.AnimationCoordinator.Reels[reel]; rv.Snap(stop);
                    Assert.That(rv.Cell(0).sprite, Is.SameAs(visual.Resolve(mapping.Symbol)));
                    for (int row = 0; row < 3; row++) Assert.That(rv.Cell(row).gameObject.activeInHierarchy, Is.True);
                    Assert.That(rv.GetComponent<UnityEngine.UI.RectMask2D>(), Is.Not.Null);
                }
            }
            Assert.That(view.SpinButton.interactable, Is.True); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ActualSpinSavesDebitAnimatesStaggersThenConfirmsExactGrid()
        {
            yield return Load(); var before = boot.Session.Snapshot; view.SpinButton.onClick.Invoke(); var spin = boot.SlotPresenter.ActiveSpin;
            Assert.That(spin, Is.Not.Null); Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Spinning));
            var saved = boot.SaveRepository.Load().Data;
            Assert.That(saved.Balance, Is.EqualTo(before.Balance - before.SelectedBet)); Assert.That(saved.PendingSpin.SpinId, Is.EqualTo(spin.SpinId));
            Assert.That(saved.LastWin, Is.EqualTo(before.LastWin)); Assert.That(view.WinText, Does.Contain(before.LastWin.ToString("N0")));
            Assert.That(view.BackButton.interactable || view.BetUpButton.interactable || view.SpinButton.interactable, Is.False);
            view.BackButton.onClick.Invoke(); Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.Slot));
            for (int i = 0; i < 20; i++) view.SpinButton.onClick.Invoke();
            Assert.That(boot.SlotPresenter.ActiveSpin, Is.SameAs(spin));
            var reels = view.AnimationCoordinator.Reels; float start = reels[0].ScrollPosition;
            yield return new WaitForSeconds(.25f);
            Assert.That(reels.All(x => x.IsAnimating), Is.True); Assert.That(reels[0].ScrollPosition, Is.Not.EqualTo(start));
            bool[] stopped = new bool[5]; int next = 0; float deadline = Time.realtimeSinceStartup + 5;
            while (view.AnimationCoordinator.IsRunning && Time.realtimeSinceStartup < deadline)
            {
                for (int i = 0; i < 5; i++) if (!reels[i].IsAnimating && !stopped[i]) {
                    Assert.That(i, Is.EqualTo(next++)); stopped[i] = true;
                    if (i < 4) { Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Spinning)); Assert.That(boot.Session.Snapshot.LastSettledSpinId, Is.Not.EqualTo(spin.SpinId)); }
                }
                yield return null;
            }
            Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Idle));
            for (int reel = 0; reel < 5; reel++) {
                Assert.That(reels[reel].StopPosition, Is.EqualTo(spin.Evaluation.Outcome.Stops[reel].Position));
                for (int row = 0; row < 3; row++) Assert.That(reels[reel].DisplayedSymbol(row), Is.EqualTo(spin.Evaluation.Outcome.Grid[reel, row]));
            }
            Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(before.Balance - before.SelectedBet + spin.PotentialPayout));
            Assert.That(view.WinText, Does.Contain(spin.PotentialPayout.ToString("N0"))); Assert.That(view.BackButton.interactable, Is.True);
            Assert.That(view.Paylines.WinningLineCount, Is.EqualTo(spin.Evaluation.Wins.WinningLines.Count));
            view.BackButton.onClick.Invoke(); Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.MainMenu)); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator EveryReelWrapsFrom29To0And1()
        {
            yield return Load(); Control(new[] { 29, 29, 29, 29, 29 }); view.SpinButton.onClick.Invoke(); var spin = controlled.ActiveSpin;
            yield return Finish(controlledSession);
            for (int reel = 0; reel < 5; reel++) for (int row = 0; row < 3; row++) {
                var rv = view.AnimationCoordinator.Reels[reel]; Assert.That(rv.StopPosition, Is.EqualTo(29));
                Assert.That(rv.DisplayedSymbol(row), Is.EqualTo(spin.Configuration.ReelStrips[reel].Symbols[(29 + row) % 30]));
                Assert.That(rv.Cell(row).sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<SlotVisualConfigSO>("Assets/_Project/Configs/SlotVisualConfigSO.asset").Resolve(spin.Evaluation.Outcome.Grid[reel, row])));
            }
        }
        [UnityTest] public IEnumerator FileSettlementFailureShowsRetryWithoutOptimisticWinOrNewSpin()
        {
            yield return Load(); view.SpinButton.onClick.Invoke(); var spin = boot.SlotPresenter.ActiveSpin;
            using (var locked = new FileStream(Path.Combine(directory, "save.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                yield return Finish(boot.Session); Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Error));
                Assert.That(view.Message, Does.Contain("not confirmed")); Assert.That(view.RetryButton.gameObject.activeSelf, Is.True);
                Assert.That(view.SpinButton.interactable || view.BackButton.interactable || view.BetUpButton.interactable, Is.False);
                Assert.That(boot.Session.Snapshot.LastWin, Is.Zero); Assert.That(view.Paylines.WinningLineCount, Is.Zero);
                view.SpinButton.onClick.Invoke(); view.BackButton.onClick.Invoke(); view.RetryButton.onClick.Invoke();
                Assert.That(boot.SlotPresenter.ActiveSpin, Is.SameAs(spin));
            }
            view.RetryButton.onClick.Invoke(); Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Idle));
            Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(19800 + spin.PotentialPayout));
            Assert.That(boot.Session.Snapshot.LastSettledSpinId, Is.EqualTo(spin.SpinId));
            Assert.That(view.AnimationCoordinator.IsRunning, Is.False); Assert.That(view.RetryButton.gameObject.activeSelf, Is.False);
            view.RetryButton.onClick.Invoke(); Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(19800 + spin.PotentialPayout));
        }
        [UnityTest] public IEnumerator FailedAcceptanceRetryPreservesPendingSpinAndDebitsOnlyOnce()
        {
            yield return Load();
            using (var locked = new FileStream(Path.Combine(directory, "save.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                view.SpinButton.onClick.Invoke(); Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Error));
                Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(20000)); Assert.That(view.AnimationCoordinator.IsRunning, Is.False);
                Assert.That(view.RetryButton.gameObject.activeSelf, Is.True);
            }
            view.RetryButton.onClick.Invoke(); Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(19800));
            Assert.That(view.AnimationCoordinator.IsRunning, Is.True); yield return Finish(boot.Session);
            Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Idle));
        }
        [UnityTest] public IEnumerator FreeSpinRetriggersHighlightScatterAndNeverAutoloops()
        {
            yield return Load(); var positions = new int[5];
            for (int reel = 0; reel < 5; reel++) positions[reel] = Enumerable.Range(0, 30).First(i => boot.RuntimeConfig.ReelStrips[reel].Symbols[i] == SymbolId.SCATTER);
            var random = Control(positions, 2); view.BetUpButton.onClick.Invoke();
            Assert.That(view.BetText, Does.Contain(600L.ToString("N0"))); view.SpinButton.onClick.Invoke(); var spin = controlled.ActiveSpin;
            Assert.That(controlledSession.Snapshot.Balance, Is.EqualTo(20000)); Assert.That(controlledSave.Data.FreeSpinsRemaining, Is.EqualTo(1));
            Assert.That(spin.EffectiveBet, Is.EqualTo(600)); yield return Finish(controlledSession);
            Assert.That(controlledSession.Snapshot.FreeSpinsRemaining, Is.EqualTo(21)); Assert.That(view.Message, Does.Contain("+20 Free Spins"));
            for (int i = 0; i < 5; i++) Assert.That(view.AnimationCoordinator.Reels[i].Cell(0).color, Is.EqualTo(new Color(.45f, 1, 1)));
            yield return new WaitForSeconds(.3f); Assert.That(random.Calls, Is.EqualTo(5)); Assert.That(view.BackButton.interactable, Is.True);
            view.SpinButton.onClick.Invoke(); Assert.That(random.Calls, Is.EqualTo(10)); Assert.That(view.Paylines.WinningLineCount, Is.Zero);
        }
        [UnityTest] public IEnumerator ConfirmedWinningCoordinatesHighlightMultipleLinesAndClearNextSpin()
        {
            yield return Load(); var positions = new int[5];
            for (int reel = 0; reel < 5; reel++) positions[reel] = Enumerable.Range(0, 30).First(i => boot.RuntimeConfig.ReelStrips[reel].Symbols[(i + 1) % 30] == SymbolId.S1);
            Control(positions); view.SpinButton.onClick.Invoke(); var spin = controlled.ActiveSpin;
            Assert.That(view.Paylines.WinningLineCount, Is.Zero); yield return Finish(controlledSession);
            Assert.That(spin.Evaluation.Wins.WinningLines.Count, Is.GreaterThan(1));
            Assert.That(view.Paylines.WinningLineCount, Is.EqualTo(spin.Evaluation.Wins.WinningLines.Count));
            foreach (var line in spin.Evaluation.Wins.WinningLines) foreach (var cell in line.Coordinates)
                Assert.That(view.AnimationCoordinator.Reels[cell.ReelIndex].Cell(cell.RowIndex).color, Is.EqualTo(new Color(1, .86f, .4f)));
            view.SpinButton.onClick.Invoke(); Assert.That(view.Paylines.WinningLineCount, Is.Zero);
            foreach (var reel in view.AnimationCoordinator.Reels) for (int row = 0; row < 3; row++) Assert.That(reel.Cell(row).color, Is.EqualTo(Color.white));
        }
        [UnityTest] public IEnumerator PauseResumesLiveAnimationWithoutPrematureSettlement()
        {
            yield return Load(); view.SpinButton.onClick.Invoke(); var spin = boot.SlotPresenter.ActiveSpin;
            boot.SendMessage("OnApplicationPause", true); Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Spinning)); Assert.That(boot.Session.Snapshot.LastSettledSpinId, Is.Not.EqualTo(spin.SpinId));
            Time.timeScale = 1; boot.SendMessage("OnApplicationPause", false); yield return Finish(boot.Session);
            Assert.That(boot.Session.Snapshot.LastSettledSpinId, Is.EqualTo(spin.SpinId));
        }
        [UnityTest] public IEnumerator DisableCancelsAtAuthoritativeStopsWithoutSettlement()
        {
            yield return Load(); view.SpinButton.onClick.Invoke(); var spin = boot.SlotPresenter.ActiveSpin;
            view.gameObject.SetActive(false); yield return new WaitForSeconds(2.5f);
            Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Spinning)); Assert.That(boot.SaveRepository.Load().Data.PendingSpin.SpinId, Is.EqualTo(spin.SpinId));
            Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(19800)); Assert.That(boot.Session.Snapshot.LastWin, Is.Zero);
            for (int i = 0; i < 5; i++) Assert.That(view.AnimationCoordinator.Reels[i].StopPosition, Is.EqualTo(spin.Evaluation.Outcome.Stops[i].Position));
            yield return Load(); Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Idle)); Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(19800));
        }
        [UnityTest] public IEnumerator DestroyedViewCannotInvokeDelayedSettlementAndReloadLosesOnlyBet()
        {
            yield return Load(); view.SpinButton.onClick.Invoke(); var session = boot.Session;
            Object.Destroy(view.gameObject); yield return null; yield return new WaitForSeconds(2.5f);
            Assert.That(session.Snapshot.LastSettledSpinId, Is.Null); Assert.That(session.Snapshot.Balance, Is.EqualTo(19800));
            yield return Load(); Assert.That(boot.Session.Snapshot.PendingSpin, Is.Null); Assert.That(boot.Session.Snapshot.LastWin, Is.Zero);
            Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(19800));
        }
        [UnityTest] public IEnumerator SavedUnstartedFreeSpinsSurviveMenuAndReload()
        {
            var config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            var profile = InitialProfileFactory.Create(config); profile.FreeSpinsRemaining = 3; profile.BonusBet = 800;
            Assert.That(new JsonSaveRepository(directory, config).Save(profile).Success, Is.True);
            yield return Load(); Assert.That(view.BetText, Does.Contain(800L.ToString("N0"))); view.BackButton.onClick.Invoke();
            Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.MainMenu)); yield return Load();
            Assert.That(boot.Session.Snapshot.FreeSpinsRemaining, Is.EqualTo(3)); view.SpinButton.onClick.Invoke();
            Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(20000)); Assert.That(boot.Session.Snapshot.FreeSpinsRemaining, Is.EqualTo(2));
            yield return Finish(boot.Session);
        }
        [UnityTest] public IEnumerator BetAndDailyRewardRefreshThroughActualControls()
        {
            yield return Load(); view.BetUpButton.onClick.Invoke(); Assert.That(boot.Session.Snapshot.SelectedBet, Is.EqualTo(400));
            view.BetDownButton.onClick.Invoke(); Assert.That(boot.Session.Snapshot.SelectedBet, Is.EqualTo(200));
            view.BackButton.onClick.Invoke(); nav.DailyBonusButton.onClick.Invoke(); nav.DailyBonusView.ClaimButton.onClick.Invoke();
            nav.CloseDailyBonusButton.onClick.Invoke(); nav.PlayButton.onClick.Invoke();
            Assert.That(view.BalanceText, Does.Contain(20400L.ToString("N0"))); Assert.That(view.SpinButton.interactable, Is.True);
        }
        [UnityTest] public IEnumerator InsufficientBalanceBlocksSpinButAllowsLowerBetAndBack()
        {
            var config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            var profile = InitialProfileFactory.Create(config); profile.Balance = 200; profile.SelectedBet = 1000;
            Assert.That(new JsonSaveRepository(directory, config).Save(profile).Success, Is.True);
            yield return Load(); Assert.That(view.SpinButton.interactable, Is.False);
            Assert.That(view.BetDownButton.interactable && view.BackButton.interactable, Is.True);
            view.SpinButton.onClick.Invoke(); Assert.That(view.AnimationCoordinator.IsRunning, Is.False);
            for (int i = 0; i < 4; i++) view.BetDownButton.onClick.Invoke();
            Assert.That(boot.Session.Snapshot.SelectedBet, Is.EqualTo(200)); Assert.That(view.SpinButton.interactable, Is.True);
            view.BackButton.onClick.Invoke(); Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.MainMenu));
        }
    }
}
#endif
