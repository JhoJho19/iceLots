#if UNITY_EDITOR
using System.Collections;
using IceP.Application;
using IceP.Bootstrap;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IceP.Tests
{
    public sealed class GameSceneSmokeTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/SC.unity";
        private string testDirectory;
        private string previousDirectory;

        [SetUp]
        public void IsolateSaveDirectory()
        {
            previousDirectory = System.Environment.GetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY");
            testDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IceP-Smoke-" + System.Guid.NewGuid().ToString("N"));
            System.Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", testDirectory);
        }

        [TearDown]
        public void CleanupSaveDirectory()
        {
            System.Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", previousDirectory);
            if (System.IO.Directory.Exists(testDirectory)) System.IO.Directory.Delete(testDirectory, true);
            else if (System.IO.File.Exists(testDirectory)) System.IO.File.Delete(testDirectory);
        }

        [UnityTest]
        public IEnumerator StartupNavigationModalsAndReloadWorkInOneScene()
        {
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var scene = SceneManager.GetActiveScene();
            var view = Object.FindFirstObjectByType<GameNavigationView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.HasRequiredReferences, Is.True);
            Assert.That(Object.FindObjectsByType<SlotBootstrapper>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            var bootstrapper = Object.FindFirstObjectByType<SlotBootstrapper>();
            Assert.That(bootstrapper.IsInitialized, Is.True);
            Assert.That(bootstrapper.OutcomeGenerator, Is.Not.Null);
            Assert.That(bootstrapper.DailyBonus, Is.Not.Null);
            Assert.That(bootstrapper.WeeklyJackpot, Is.Not.Null);
            var jackpotStatus = bootstrapper.WeeklyJackpot.GetStatus();
            Assert.That(jackpotStatus.EventId, Is.EqualTo(bootstrapper.RuntimeConfig.Jackpot.EventId));
            Assert.That(jackpotStatus.JackpotAmount, Is.EqualTo(bootstrapper.RuntimeConfig.Jackpot.Amount));
            Assert.That(jackpotStatus.IsEligible, Is.True);
            Assert.That(bootstrapper.Clock, Is.Not.Null);
            Assert.That(bootstrapper.RuntimeConfig.ConfigVersion, Is.EqualTo("1.0.0"));
            Assert.That(bootstrapper.LoadedProfile.Balance, Is.EqualTo(20000));
            Assert.That(bootstrapper.LoadedProfile.PendingSpin, Is.Null);
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(testDirectory, "save.json")), Is.True);
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(EventSystem.current.GetComponent<InputSystemUIInputModule>(), Is.Not.Null);
            var menu = GetScreen(view, "mainMenuScreen");
            var slot = GetScreen(view, "slotScreen");
            var settings = GetScreen(view, "settingsPopup");
            var daily = GetScreen(view, "dailyBonusPopup");
            Assert.That(menu.activeSelf, Is.True);
            Assert.That(slot.activeSelf || settings.activeSelf || daily.activeSelf, Is.False);

            view.SettingsButton.onClick.Invoke();
            Assert.That(settings.activeSelf, Is.True);
            Assert.That(view.PlayButton.IsInteractable(), Is.False);
            view.CloseSettingsButton.onClick.Invoke();
            Assert.That(settings.activeSelf, Is.False);
            Assert.That(view.PlayButton.IsInteractable(), Is.True);
            view.DailyBonusButton.onClick.Invoke();
            Assert.That(daily.activeSelf, Is.True);
            Assert.That(view.PlayButton.IsInteractable(), Is.False);
            view.CloseDailyBonusButton.onClick.Invoke();
            Assert.That(daily.activeSelf, Is.False);

            for (var i = 0; i < 3; i++)
            {
                view.PlayButton.onClick.Invoke();
                Assert.That(menu.activeSelf, Is.False);
                Assert.That(slot.activeSelf, Is.True);
                Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(scene.handle));
                view.BackButton.onClick.Invoke();
                Assert.That(menu.activeSelf, Is.True);
                Assert.That(slot.activeSelf, Is.False);
            }
            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));

            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            view = Object.FindFirstObjectByType<GameNavigationView>();
            Assert.That(Object.FindFirstObjectByType<SlotBootstrapper>().IsInitialized, Is.True);
            Assert.That(GetScreen(view, "mainMenuScreen").activeSelf, Is.True);
            Assert.That(Object.FindObjectsByType<SlotBootstrapper>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            view.PlayButton.onClick.Invoke();
            Assert.That(GetScreen(view, "slotScreen").activeSelf, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CorruptedProfileBlocksStartupAndPlayCommand()
        {
            System.IO.Directory.CreateDirectory(testDirectory);
            System.IO.File.WriteAllText(System.IO.Path.Combine(testDirectory, "save.json"), "{broken");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SlotBootstrapper initialization failed: No valid committed snapshot"));
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var bootstrapper = Object.FindFirstObjectByType<SlotBootstrapper>();
            var view = Object.FindFirstObjectByType<GameNavigationView>();
            Assert.That(bootstrapper.IsInitialized, Is.False);
            Assert.That(bootstrapper.LoadedProfile, Is.Null);
            Assert.That(bootstrapper.InitializationError, Does.Contain("Explicit profile reset"));
            Assert.That(view.PlayButton.interactable, Is.False);
            view.PlayButton.onClick.Invoke();
            Assert.That(GetScreen(view, "mainMenuScreen").activeSelf, Is.True);
            Assert.That(GetScreen(view, "slotScreen").activeSelf, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator InitialSaveFailureBlocksStartup()
        {
            // A file occupies the intended directory: deterministic initial-write failure.
            System.IO.File.WriteAllText(testDirectory, "blocking file");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SlotBootstrapper initialization failed:"));
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var bootstrapper = Object.FindFirstObjectByType<SlotBootstrapper>();
            Assert.That(bootstrapper.IsInitialized, Is.False);
            Assert.That(bootstrapper.InitializationError, Is.Not.Empty);
            Assert.That(Object.FindFirstObjectByType<GameNavigationView>().PlayButton.interactable, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ApplicationSpinBlocksBackAndReloadRecoversAcceptedDebit()
        {
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            var view = Object.FindFirstObjectByType<GameNavigationView>();
            Assert.That(boot.Spins.SelectBet(1000).Success, Is.True);
            view.PlayButton.onClick.Invoke();
            var start = boot.Spins.StartSpin();
            Assert.That(start.Success, Is.True, start.Diagnostic);
            Assert.That(start.Spin.Jackpot.Participated, Is.True);
            yield return null;
            Assert.That(view.BackButton.interactable, Is.False);
            view.BackButton.onClick.Invoke(); // Invoke bypasses interactable.
            boot.Navigation.NavigateToMainMenu();
            Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.Slot));
            Assert.That(GetScreen(view, "slotScreen").activeSelf, Is.True);
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(19000));
            Assert.That(boot.SaveRepository.Load().Data.PendingSpin.SpinId, Is.EqualTo(start.Spin.SpinId));
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            Assert.That(boot.IsInitialized, Is.True);
            Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Idle));
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(19000));
            Assert.That(boot.LoadedProfile.LastWin, Is.Zero);
            Assert.That(boot.LoadedProfile.PendingSpin, Is.Null);
            Assert.That(boot.SaveRepository.Load().Data.PendingSpin, Is.Null);
            Assert.That(boot.Spins.StartSpin().Success, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StartupRecoveryFailureBlocksNavigationAndRetryUsesSameSnapshot()
        {
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            boot.Spins.SelectBet(1000);
            Assert.That(boot.Spins.StartSpin().Success, Is.True);
            var temporary = System.IO.Path.Combine(testDirectory, "save.json.tmp");
            System.IO.Directory.CreateDirectory(temporary); // Prevent recovery's temp-file write.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SlotBootstrapper initialization failed:"));
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            var view = Object.FindFirstObjectByType<GameNavigationView>();
            Assert.That(boot.IsInitialized, Is.False);
            Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(boot.Spins.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            Assert.That(boot.LoadedProfile.PendingSpin, Is.Not.Null);
            Assert.That(boot.SaveRepository.Load().Data.Balance, Is.EqualTo(19000));
            Assert.That(view.PlayButton.interactable, Is.False);
            view.PlayButton.onClick.Invoke();
            Assert.That(GetScreen(view, "mainMenuScreen").activeSelf, Is.True);
            System.IO.Directory.Delete(temporary);
            Assert.That(boot.RetryInitializationRecovery(), Is.True);
            Assert.That(boot.IsInitialized, Is.True);
            Assert.That(boot.InitializationError, Is.Null);
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(19000));
            Assert.That(boot.LoadedProfile.PendingSpin, Is.Null);
            Assert.That(boot.SaveRepository.Load().Data.PendingSpin, Is.Null);
            view.PlayButton.onClick.Invoke();
            Assert.That(GetScreen(view, "slotScreen").activeSelf, Is.True);
            Assert.That(boot.RetryInitializationRecovery(), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DailyClaimUpdatesAuthoritativeBalanceAndSurvivesSceneReload()
        {
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            Assert.That(boot.IsInitialized, Is.True);
            Assert.That(boot.Spins, Is.Not.Null);
            Assert.That(boot.DailyBonus.GetStatus().CanClaim, Is.True);
            Assert.That(boot.LoadedProfile.DailyLastClaimUtc, Is.Null); // bootstrap never auto-claims
            var claim = boot.DailyBonus.Claim();
            Assert.That(claim.Success, Is.True, claim.Diagnostic);
            long balance = 20000 + boot.RuntimeConfig.DailyBonus.Rewards[0];
            Assert.That(claim.ConfirmedBalance, Is.EqualTo(balance));
            Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(balance));
            Assert.That(boot.SaveRepository.Load().Data.Balance, Is.EqualTo(balance));
            Assert.That(boot.SaveRepository.Load().Data.DailyLastClaimUtc, Is.EqualTo(claim.ClaimUtc));
            Assert.That(boot.SaveRepository.Load().Data.DailyStreakDay, Is.EqualTo(1));
            Assert.That(boot.DailyBonus.Claim().Error, Is.EqualTo(DailyBonusError.AlreadyClaimed));
            var view = Object.FindFirstObjectByType<GameNavigationView>();
            Assert.That(GetScreen(view, "mainMenuScreen").activeSelf, Is.True);
            Assert.That(GetScreen(view, "dailyBonusPopup").activeSelf, Is.False);
            view.PlayButton.onClick.Invoke();
            view.BackButton.onClick.Invoke();
            Assert.That(GetScreen(view, "mainMenuScreen").activeSelf, Is.True);

            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            Assert.That(boot.IsInitialized, Is.True);
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(balance));
            Assert.That(boot.LoadedProfile.DailyStreakDay, Is.EqualTo(1));
            Assert.That(boot.LoadedProfile.DailyLastClaimUtc, Is.EqualTo(claim.ClaimUtc));
            Assert.That(boot.DailyBonus.Claim().Error, Is.EqualTo(DailyBonusError.AlreadyClaimed));
            Assert.That(boot.SaveRepository.Load().Data.Balance, Is.EqualTo(balance));
            var spin = boot.Spins.StartSpin();
            Assert.That(spin.Success, Is.True, spin.Diagnostic);
            Assert.That(boot.Session.Snapshot.Balance, Is.EqualTo(balance - boot.LoadedProfile.SelectedBet));
            Assert.That(boot.DailyBonus.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
            LogAssert.NoUnexpectedReceived();
        }

        private sealed class WinningBits : IceP.Domain.SlotEngine.IRandomSource
        {
            public int NextInt(int min, int max) => min;
        }

        [UnityTest]
        public IEnumerator DeterministicJackpotSettlementSurvivesSceneReload()
        {
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            // Detached test composition, using the bootstrapped config/clock/repository.
            // The production service is idle throughout; no test seed or probability override.
            var session = new GameSession(boot.SaveRepository.Load().Data);
            var jackpot = new WeeklyJackpotService(session, boot.RuntimeConfig, boot.Clock, new WinningBits());
            var spins = new SpinService(session, boot.RuntimeConfig,
                new IceP.Domain.SlotEngine.SlotEngine(boot.RuntimeConfig, new WinningBits()),
                new BalanceService(boot.RuntimeConfig), boot.SaveRepository, () => boot.Clock.UtcNow, jackpot);
            Assert.That(spins.SelectBet(1000).Success, Is.True);
            var start = spins.StartSpin(); Assert.That(start.Success, Is.True, start.Diagnostic);
            Assert.That(start.Spin.Jackpot.JackpotWon, Is.True);
            Assert.That(session.Snapshot.Balance, Is.EqualTo(19000));
            Assert.That(session.Snapshot.JackpotWinsThisWeek, Is.Zero);
            var done = spins.CompleteSpin(start.Spin.SpinId); Assert.That(done.Success, Is.True, done.Diagnostic);
            var saved = boot.SaveRepository.Load().Data;
            Assert.That(saved.Balance, Is.EqualTo(19000 + start.Spin.PotentialPayout));
            Assert.That(saved.JackpotWinsThisWeek, Is.EqualTo(1));
            Assert.That(saved.JackpotWeekId, Is.EqualTo(start.Spin.JackpotWeekId));
            Assert.That(saved.PendingSpin, Is.Null);
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(saved.Balance));
            Assert.That(boot.LoadedProfile.LastWin, Is.EqualTo(start.Spin.PotentialPayout));
            Assert.That(boot.LoadedProfile.JackpotWinsThisWeek, Is.EqualTo(1));
            Assert.That(boot.WeeklyJackpot.GetStatus().IsEligible, Is.False);
            Assert.That(boot.SaveRepository.Load().Data.JackpotEventId, Is.EqualTo(start.Spin.Jackpot.EventId));
            Assert.That(boot.Spins.CompleteSpin(start.Spin.SpinId).AlreadySettled, Is.True);
            Assert.That(boot.DailyBonus.GetStatus().CanClaim, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        private static GameObject GetScreen(GameNavigationView view, string field)
        {
            // Follow the Inspector references so existing UI names/layout can change independently.
            return (GameObject)new SerializedObject(view).FindProperty(field).objectReferenceValue;
        }
    }
}
#endif
