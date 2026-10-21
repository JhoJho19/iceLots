#if UNITY_EDITOR
using System.Collections;
using System.IO;
using IceP.Application;
using IceP.Bootstrap;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IceP.Tests
{
    public sealed class MainMenuUiTests
    {
        private string directory;
        private string previous;
        private SlotBootstrapper boot;
        private GameNavigationView view;
        [SetUp] public void Setup()
        {
            previous = System.Environment.GetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY");
            directory = Path.Combine(Path.GetTempPath(), "IceP-MenuUI-" + System.Guid.NewGuid().ToString("N"));
            System.Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", directory);
        }
        [TearDown] public void Cleanup()
        {
            System.Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", previous);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private IEnumerator Load()
        {
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/SC.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            boot = Object.FindFirstObjectByType<SlotBootstrapper>(); view = Object.FindFirstObjectByType<GameNavigationView>();
            Assert.That(boot.IsInitialized, Is.True); Assert.That(view.HasRequiredReferences, Is.True);
        }
        [UnityTest] public IEnumerator ConcreteWalkthroughPersistsRewardAndAudioThroughActualControls()
        {
            yield return Load(); var session = boot.Session; var scene = SceneManager.GetActiveScene().handle;
            Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.MainMenu)); Assert.That(view.DailyAvailable, Is.True);
            Assert.That(view.DailyBonusButton.interactable, Is.True);
            Assert.That(view.DailyBonusButton.transform.Find("AvailableIndicator"), Is.Null);
            var availableMaterial = view.DailyBonusButton.image.material;
            Assert.That(availableMaterial.name, Is.EqualTo("ShiningIce 1"));
            // Start at the rest scale; scene loading may already have advanced the loop.
            view.ShowDailyAvailability(false);
            Assert.That(view.DailyBonusButton.image.material, Is.SameAs(UnityEngine.UI.Graphic.defaultGraphicMaterial));
            var baseScale = view.DailyBonusButton.transform.localScale;
            view.ShowDailyAvailability(true);
            Assert.That(view.DailyBonusButton.image.material, Is.SameAs(availableMaterial));
            view.TickDailyAttention(.4f);
            Assert.That(view.DailyBonusButton.transform.localScale.x, Is.GreaterThan(baseScale.x));
            view.DailyBonusButton.onClick.Invoke(); var daily = view.DailyBonusView;
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20000)); Assert.That(daily.RewardLabels.Length, Is.EqualTo(7));
            for (int i = 0; i < 7; i++) Assert.That(daily.RewardLabels[i].text, Does.Contain(boot.RuntimeConfig.DailyBonus.Rewards[i].ToString("N0")));
            daily.ClaimButton.onClick.Invoke(); Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20400));
            Assert.That(daily.ClaimButton.interactable, Is.False); Assert.That(view.DailyAvailable, Is.False);
            Assert.That(view.DailyBonusButton.interactable, Is.False);
            Assert.That(view.DailyBonusButton.image.material, Is.SameAs(UnityEngine.UI.Graphic.defaultGraphicMaterial));
            Assert.That(view.DailyBonusButton.transform.localScale, Is.EqualTo(baseScale));
            Assert.That(view.BalanceText, Does.Contain(20400L.ToString("N0"))); Assert.That(daily.Message, Does.Contain("Received"));
            Assert.That(daily.GetComponentsInChildren<Transform>(true), Has.None.Matches<Transform>(x => x.name == "RewardStatus"));
            daily.ClaimButton.onClick.Invoke(); Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20400));
            view.CloseDailyBonusButton.onClick.Invoke();
            view.SettingsButton.onClick.Invoke(); var settings = view.SettingsView;
            settings.MusicVolume.value = .35f; settings.SoundVolume.value = .65f;
            // Slider values remain local until debounce/close, and then use one whole-snapshot command.
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(1));
            view.CloseSettingsButton.onClick.Invoke();
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(.35).Within(.0001));
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20400));
            view.PlayButton.onClick.Invoke(); Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.Slot));
            Assert.That(boot.Session, Is.SameAs(session)); Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(scene));
            view.BackButton.onClick.Invoke(); Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.MainMenu));
            yield return Load(); Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20400)); Assert.That(view.DailyAvailable, Is.False);
            Assert.That(boot.Settings.Current.MusicEnabled && boot.Settings.Current.SoundEnabled, Is.True);
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(.35).Within(.0001)); Assert.That(boot.Settings.Current.SoundVolume, Is.EqualTo(.65).Within(.0001));
            Assert.That(view.DailyBonusButton.interactable, Is.False);
            Assert.That(view.DailyBonusButton.image.material, Is.SameAs(UnityEngine.UI.Graphic.defaultGraphicMaterial));
            view.DailyBonusButton.onClick.Invoke(); Assert.That(view.HasOpenPopup, Is.False);
            view.CloseDailyBonusButton.onClick.Invoke(); view.PlayButton.onClick.Invoke();
            Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.Slot)); Assert.That(SceneManager.sceneCount, Is.EqualTo(1)); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator RepeatedOpeningKeepsSingleModalAndSingleClaim()
        {
            yield return Load();
            for (int i = 0; i < 5; i++)
            {
                view.SettingsButton.onClick.Invoke(); view.DailyBonusButton.onClick.Invoke(); view.PlayButton.onClick.Invoke();
                Assert.That(view.SettingsView.gameObject.activeSelf, Is.True); Assert.That(view.DailyBonusView.gameObject.activeSelf, Is.False);
                Assert.That(view.PlayButton.IsInteractable(), Is.False); Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.MainMenu));
                view.CloseSettingsButton.onClick.Invoke(); view.DailyBonusButton.onClick.Invoke(); view.SettingsButton.onClick.Invoke();
                Assert.That(view.SettingsView.gameObject.activeSelf, Is.False); Assert.That(view.DailyBonusView.gameObject.activeSelf, Is.True);
                view.CloseDailyBonusButton.onClick.Invoke();
            }
            view.DailyBonusButton.onClick.Invoke(); view.DailyBonusView.ClaimButton.onClick.Invoke();
            Assert.That(view.DailyBonusView.Message, Does.Contain("Received")); // Duplicate listeners would replace it with AlreadyClaimed.
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20400));
            view.CloseDailyBonusButton.onClick.Invoke(); view.DailyBonusButton.onClick.Invoke();
            Assert.That(view.DailyBonusButton.interactable, Is.False);
            Assert.That(view.DailyBonusButton.image.material, Is.SameAs(UnityEngine.UI.Graphic.defaultGraphicMaterial)); Assert.That(view.HasOpenPopup, Is.False); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator SettingsDebounceAndResumeRefreshWork()
        {
            yield return Load(); view.SettingsButton.onClick.Invoke(); var settings = view.SettingsView;
            settings.MusicVolume.value = .2f; settings.MusicVolume.value = .4f; settings.MusicVolume.value = .6f;
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(.6).Within(.0001));
            view.CloseSettingsButton.onClick.Invoke();
            Assert.That(boot.DailyBonus.Claim().Success, Is.True); // External confirmed application update.
            boot.SendMessage("OnApplicationPause", false); Assert.That(view.DailyAvailable, Is.False);
            Assert.That(view.DailyBonusButton.interactable, Is.False);
            Assert.That(view.DailyBonusButton.image.material, Is.SameAs(UnityEngine.UI.Graphic.defaultGraphicMaterial));
            view.DailyBonusButton.onClick.Invoke(); Assert.That(view.HasOpenPopup, Is.False); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator SlidersMuteAndUnmuteAudioAcrossReload()
        {
            yield return Load(); view.SettingsButton.onClick.Invoke(); var settings = view.SettingsView;
            Assert.That(settings.HasRequiredReferences, Is.True);
            settings.MusicVolume.value = 0; settings.SoundVolume.value = 0;
            view.CloseSettingsButton.onClick.Invoke();
            yield return Load();
            Assert.That(boot.Settings.Current.MusicEnabled || boot.Settings.Current.SoundEnabled, Is.False);
            Assert.That(boot.Settings.Current.MusicVolume, Is.Zero); Assert.That(boot.Settings.Current.SoundVolume, Is.Zero);
            view.SettingsButton.onClick.Invoke(); settings = view.SettingsView;
            settings.MusicVolume.value = .35f; settings.SoundVolume.value = .65f;
            view.CloseSettingsButton.onClick.Invoke();
            yield return Load();
            Assert.That(boot.Settings.Current.MusicEnabled && boot.Settings.Current.SoundEnabled, Is.True);
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(.35).Within(.0001));
            Assert.That(boot.Settings.Current.SoundVolume, Is.EqualTo(.65).Within(.0001));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator LegacyMutedSettingsShowZeroAndCanBeUnmutedWithSliders()
        {
            yield return Load();
            var saved = boot.Settings.Current;
            saved.MusicEnabled = saved.SoundEnabled = false;
            saved.MusicVolume = .4; saved.SoundVolume = .7;
            Assert.That(boot.Settings.Update(saved).Success, Is.True);
            view.SettingsButton.onClick.Invoke(); var settings = view.SettingsView;
            Assert.That(settings.MusicVolume.value, Is.Zero); Assert.That(settings.SoundVolume.value, Is.Zero);
            settings.MusicVolume.value = .2f;
            view.CloseSettingsButton.onClick.Invoke();
            Assert.That(boot.Settings.Current.MusicEnabled, Is.True);
            Assert.That(boot.Settings.Current.SoundEnabled, Is.False);
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(.2).Within(.0001));
            Assert.That(boot.Settings.Current.SoundVolume, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ActiveSpinCannotBeInterruptedAndPopupsCannotMutateIt()
        {
            yield return Load(); view.PlayButton.onClick.Invoke(); var spin = boot.Spins.StartSpin(); Assert.That(spin.Success, Is.True);
            yield return null; Assert.That(view.BackButton.interactable, Is.False); view.BackButton.onClick.Invoke();
            view.SettingsButton.onClick.Invoke(); view.DailyBonusButton.onClick.Invoke();
            Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.Slot)); Assert.That(view.HasOpenPopup, Is.False);
            var prefs = boot.Settings.Current; prefs.MusicVolume = .1;
            Assert.That(boot.Settings.Update(prefs).Error, Is.EqualTo(SettingsError.Busy));
            Assert.That(boot.DailyBonus.Claim().Error, Is.EqualTo(DailyBonusError.Busy));
            Assert.That(boot.Spins.CompleteSpin(spin.Spin.SpinId).Success, Is.True);
            Assert.That(boot.WeeklyJackpot.GetStatus(), Is.Not.Null); yield return null;
            view.BackButton.onClick.Invoke(); Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.MainMenu)); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator PendingSliderValueSurvivesSceneUnloadAndPause()
        {
            yield return Load(); view.SettingsButton.onClick.Invoke(); view.SettingsView.MusicVolume.value = .45f;
            yield return Load(); Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(.45).Within(.0001));
            view.SettingsButton.onClick.Invoke(); view.SettingsView.SoundVolume.value = .3f;
            boot.SendMessage("OnApplicationPause", true);
            Assert.That(boot.Settings.Current.SoundVolume, Is.EqualTo(.3).Within(.0001));
            boot.SendMessage("OnApplicationPause", false); yield return Load();
            Assert.That(boot.Settings.Current.SoundVolume, Is.EqualTo(.3).Within(.0001)); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator FailedClaimShowsRetryAndNoOptimisticBalance()
        {
            yield return Load(); view.DailyBonusButton.onClick.Invoke(); var daily = view.DailyBonusView;
            var rewardPanel = daily.RewardLabels[0].transform.parent.GetComponent<UnityEngine.UI.Image>();
            var defaultSprite = rewardPanel.sprite;
            var claimedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprites/Reward day claimed sprite.png");
            Assert.That(claimedSprite, Is.Not.Null);
            using (var locked = new FileStream(Path.Combine(directory, "save.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                daily.ClaimButton.onClick.Invoke(); Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Error));
                Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20000)); Assert.That(daily.Message, Does.Contain("not confirmed"));
                Assert.That(rewardPanel.sprite, Is.SameAs(defaultSprite));
                Assert.That(daily.ClaimButton.interactable, Is.False); Assert.That(daily.RetryButton.gameObject.activeSelf, Is.True);
                daily.ClaimButton.onClick.Invoke(); Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20000));
                view.CloseDailyBonusButton.onClick.Invoke(); view.PlayButton.onClick.Invoke();
                Assert.That(boot.Navigation.CurrentScreen, Is.EqualTo(GameScreen.MainMenu));
                view.DailyBonusButton.onClick.Invoke(); Assert.That(daily.RetryButton.gameObject.activeSelf, Is.True);
            }
            daily.RetryButton.onClick.Invoke(); Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20400));
            Assert.That(rewardPanel.sprite, Is.SameAs(claimedSprite));
            Assert.That(rewardPanel.color, Is.EqualTo(Color.white));
            Assert.That(daily.RewardLabels[1].transform.parent.GetComponent<UnityEngine.UI.Image>().sprite, Is.SameAs(defaultSprite));
            Assert.That(daily.Message, Does.Contain("Received")); Assert.That(view.DailyAvailable, Is.False); Assert.That(daily.RetryButton.gameObject.activeSelf, Is.False);
            view.CloseDailyBonusButton.onClick.Invoke(); view.DailyBonusButton.onClick.Invoke();
            Assert.That(rewardPanel.sprite, Is.SameAs(claimedSprite));
            daily.RetryButton.onClick.Invoke(); Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20400)); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator FailedSettingsSaveReopensWithoutRetryControl()
        {
            yield return Load(); view.SettingsButton.onClick.Invoke(); var settings = view.SettingsView;
            using (var locked = new FileStream(Path.Combine(directory, "save.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                settings.MusicVolume.value = .25f; view.CloseSettingsButton.onClick.Invoke();
                Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Error)); Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(1));
                view.SettingsButton.onClick.Invoke(); Assert.That(settings.HasRequiredReferences, Is.True);
                Assert.That(settings.GetComponentsInChildren<Transform>(true), Has.None.Matches<Transform>(x => x.name == "RetrySettings"));
                Assert.That(settings.MusicVolume.value, Is.EqualTo(1)); Assert.That(settings.MusicVolume.interactable, Is.False);
                Assert.That(settings.SoundVolume.interactable, Is.False);
                Assert.That(settings.Message, Does.Contain("not confirmed"));
                Assert.That(settings.Message, Does.Not.Contain("Retry"));
            }
            // Persistence recovery remains a service operation, without a settings UI control.
            Assert.That(boot.Settings.RetryPersistence().Success, Is.True);
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(.25));
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20000));
            view.CloseSettingsButton.onClick.Invoke(); yield return Load();
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(.25)); Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20000)); LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
