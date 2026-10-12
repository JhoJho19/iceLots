#if UNITY_EDITOR
using System.Collections;
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
            Assert.That(bootstrapper.RuntimeConfig.ConfigVersion, Is.EqualTo("1.0.0"));
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

        private static GameObject GetScreen(GameNavigationView view, string field)
        {
            // Follow the Inspector references so existing UI names/layout can change independently.
            return (GameObject)new SerializedObject(view).FindProperty(field).objectReferenceValue;
        }
    }
}
#endif
