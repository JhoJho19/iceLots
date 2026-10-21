#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using IceP.Bootstrap;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IceP.Tests
{
    public sealed class GameAudioTests
    {
        private string previousDirectory;
        private string testDirectory;

        [SetUp]
        public void Setup()
        {
            previousDirectory = Environment.GetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY");
            testDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IceP-Audio-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", testDirectory);
        }

        [TearDown]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", previousDirectory);
            if (System.IO.Directory.Exists(testDirectory)) System.IO.Directory.Delete(testDirectory, true);
        }

        private static AudioSource Source(GameAudioController audio, string name) =>
            audio.GetComponentsInChildren<AudioSource>().Single(x => x.name == name);

        [UnityTest]
        public IEnumerator SceneAudioFollowsSpinStopsAndSettingsPreview()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/SC.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<SlotBootstrapper>();
            Assert.That(bootstrap.IsInitialized, Is.True, bootstrap.InitializationError);
            var audio = UnityEngine.Object.FindFirstObjectByType<GameAudioController>();
            Assert.That(audio, Is.Not.Null);
            var music = Source(audio, "Music");
            var reels = Source(audio, "Reels");
            Assert.That(music.clip.name, Is.EqualTo("music_main_loop"));
            Assert.That(music.loop && music.isPlaying, Is.True);
            Assert.That(reels.clip.name, Is.EqualTo("reel_spin_loop"));
            var view = UnityEngine.Object.FindFirstObjectByType<GameNavigationView>();
            view.SettingsButton.onClick.Invoke();
            view.SettingsView.MusicVolume.value = 0;
            view.SettingsView.SoundVolume.value = 0;
            Assert.That(music.volume, Is.Zero, "Music slider must preview before saving.");
            Assert.That(Source(audio, "UI").volume, Is.Zero);
            view.CloseSettingsButton.onClick.Invoke();
            Assert.That(bootstrap.Settings.Current.SoundEnabled, Is.False);
            view.SettingsButton.onClick.Invoke();
            view.SettingsView.MusicVolume.value = .5f;
            view.SettingsView.SoundVolume.value = .5f;
            view.CloseSettingsButton.onClick.Invoke();
            Assert.That(music.volume, Is.EqualTo(.15f).Within(.001));
            Assert.That(Source(audio, "UI").volume, Is.EqualTo(.35f).Within(.001));

            view.PlayButton.onClick.Invoke();
            var slot = UnityEngine.Object.FindFirstObjectByType<SlotView>();
            int stopped = 0;
            slot.AnimationCoordinator.ReelStopped += (_, __) => stopped++;
            slot.SpinButton.onClick.Invoke();
            Assert.That(reels.isPlaying, Is.True);
            float deadline = Time.realtimeSinceStartup + 5;
            while (slot.AnimationCoordinator.IsRunning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(stopped, Is.EqualTo(5));
            Assert.That(reels.isPlaying, Is.False);
            Assert.That(bootstrap.LoadedProfile.PendingSpin, Is.Null);
        }

        [UnityTest]
        public IEnumerator CancelStopsReelsAndDailyRewardUsesConfirmedCelebration()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/SC.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var audio = UnityEngine.Object.FindFirstObjectByType<GameAudioController>();
            var view = UnityEngine.Object.FindFirstObjectByType<GameNavigationView>();
            view.DailyBonusButton.onClick.Invoke();
            view.DailyBonusView.ClaimButton.onClick.Invoke();
            Assert.That(Source(audio, "Rewards").isPlaying, Is.True);
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<SlotBootstrapper>();
            Assert.That(bootstrap.LoadedProfile.Balance, Is.GreaterThan(20000));
            view.CloseDailyBonusButton.onClick.Invoke();
            view.PlayButton.onClick.Invoke();
            var slot = UnityEngine.Object.FindFirstObjectByType<SlotView>();
            slot.SpinButton.onClick.Invoke();
            Assert.That(Source(audio, "Reels").isPlaying, Is.True);
            slot.CancelAnimation();
            Assert.That(Source(audio, "Reels").isPlaying, Is.False);
            Assert.That(Source(audio, "Rewards").isPlaying, Is.False);
            Assert.That(bootstrap.LoadedProfile.PendingSpin, Is.Not.Null, "Cancellation must not confirm a win.");
        }
    }
}
#endif
