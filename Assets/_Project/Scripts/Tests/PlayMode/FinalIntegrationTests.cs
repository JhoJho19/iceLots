#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Bootstrap;
using IceP.Infrastructure.Configuration;
using IceP.Infrastructure.Persistence;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace IceP.Tests
{
    public sealed class FinalIntegrationTests
    {
        private string directory, previous, captureDirectory;
        private SlotBootstrapper boot;
        private GameNavigationView nav;
        private SlotView slot;
        private const string ScenePath = "Assets/_Project/Scenes/SC.unity";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private object sizeGroup;
        private EditorWindow gameView;
        private int originalSize, addedSizes;

        [SetUp] public void Setup()
        {
            previous = Environment.GetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY");
            directory = Path.Combine(Path.GetTempPath(), "IceP-Final-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", directory);
            captureDirectory = Path.GetFullPath(Path.Combine("Logs", "Section413", "Captures-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        }
        [TearDown] public void Cleanup()
        {
            if (gameView)
            {
                gameView.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(gameView, originalSize);
                while (addedSizes-- > 0)
                {
                    int count = (int)sizeGroup.GetType().GetMethod("GetTotalCount", Instance).Invoke(sizeGroup, null);
                    sizeGroup.GetType().GetMethod("RemoveCustomSize", Instance).Invoke(sizeGroup, new object[] { count - 1 });
                }
                gameView.Repaint();
            }
            Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", previous);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private IEnumerator Load()
        {
            LogAssert.Expect(LogType.Log, "SlotBootstrapper initialized with configuration 1.0.0.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            boot = Object.FindFirstObjectByType<SlotBootstrapper>();
            nav = Object.FindFirstObjectByType<GameNavigationView>();
            slot = Object.FindFirstObjectByType<SlotView>(FindObjectsInactive.Include);
            Assert.That(boot.IsInitialized, Is.True);
            SetResolution(1920, 1080); yield return null; yield return null;
        }
        private static IEnumerator Click(Button button)
        {
            yield return null;
            Assert.That(button.IsInteractable() && button.gameObject.activeInHierarchy, Is.True, button.name);
            // Exercise EventSystem pointer dispatch and the real GraphicRaycaster, not just onClick.
            Canvas.ForceUpdateCanvases();
            var data = new PointerEventData(EventSystem.current) { position = Bounds((RectTransform)button.transform).center };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), button.name);
            var target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.That(target, Is.EqualTo(button.gameObject), button.name + " blocked by raycast");
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
        }
        private static Rect Bounds(RectTransform rect)
        {
            var points = new Vector3[4]; rect.GetWorldCorners(points);
            return Rect.MinMaxRect(points[0].x, points[0].y, points[2].x, points[2].y);
        }
        private IEnumerator Finish()
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (boot.Session.State == GameSessionState.Spinning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(boot.Session.State, Is.EqualTo(GameSessionState.Idle));
        }
        [UnityTest] public IEnumerator EntireWalkthroughUsesOneSessionAndReloadsOneConfirmedProfile()
        {
            yield return Load(); var session = boot.Session; var scene = slot.gameObject.scene.handle;
            yield return Click(nav.SettingsButton);
            nav.SettingsView.MusicEnabled.isOn = false; nav.SettingsView.SoundEnabled.isOn = false;
            nav.SettingsView.MusicVolume.value = .35f; nav.SettingsView.SoundVolume.value = .65f;
            yield return Click(nav.CloseSettingsButton); yield return Click(nav.DailyBonusButton);
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20000));
            yield return Click(nav.DailyBonusView.ClaimButton); nav.DailyBonusView.ClaimButton.onClick.Invoke();
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20400));
            yield return Click(nav.CloseDailyBonusButton); yield return Click(nav.PlayButton); yield return Click(slot.BetUpButton); yield return Click(slot.SpinButton);
            var spin = boot.SlotPresenter.ActiveSpin;
            Assert.That(boot.SaveRepository.Load().Data.Balance, Is.EqualTo(20000));
            Assert.That(boot.Session, Is.SameAs(session));
            Assert.That(slot.gameObject.scene.handle, Is.EqualTo(scene));
            Assert.That(boot.Navigation.TryNavigateToMainMenu(), Is.False);
            int objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            yield return new WaitForSeconds(.3f);
            Assert.That(Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(objects));
            yield return Finish();
            var saved = boot.SaveRepository.Load().Data;
            Assert.That(saved.Balance, Is.EqualTo(20000 + spin.PotentialPayout));
            Assert.That(saved.PendingSpin, Is.Null);
            Assert.That(slot.Paylines.WinningLineCount, Is.EqualTo(spin.Evaluation.Wins.WinningLines.Count));
            for (int reel = 0; reel < 5; reel++) for (int row = 0; row < 3; row++)
                Assert.That(slot.AnimationCoordinator.Reels[reel].DisplayedSymbol(row), Is.EqualTo(spin.Evaluation.Outcome.Grid[reel, row]));
            yield return Click(slot.BackButton); yield return Load();
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(saved.Balance));
            Assert.That(boot.LoadedProfile.LastWin, Is.EqualTo(saved.LastWin));
            Assert.That(boot.LoadedProfile.FreeSpinsRemaining, Is.EqualTo(saved.FreeSpinsRemaining));
            Assert.That(boot.LoadedProfile.DailyLastClaimUtc, Is.EqualTo(saved.DailyLastClaimUtc));
            Assert.That(boot.Settings.Current.MusicVolume, Is.EqualTo(.35).Within(.0001));
            Assert.That(boot.Settings.Current.SoundVolume, Is.EqualTo(.65).Within(.0001));
            Assert.That(boot.Settings.Current.MusicEnabled || boot.Settings.Current.SoundEnabled, Is.False);
            Assert.That(boot.Spins.CompleteSpin(spin.SpinId).AlreadySettled, Is.True);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator DisabledSlotComponentCancelsWithoutSettlementAndReloadConsumesOnlyReservedFreeSpin()
        {
            var config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            var profile = InitialProfileFactory.Create(config); profile.FreeSpinsRemaining = 3; profile.BonusBet = 800;
            Assert.That(new JsonSaveRepository(directory, config).Save(profile).Success, Is.True);
            yield return Load(); yield return Click(nav.PlayButton); yield return Click(slot.SpinButton);
            var id = boot.SlotPresenter.ActiveSpin.SpinId;
            slot.enabled = false;
            yield return new WaitForSeconds(2.5f);
            Assert.That(slot.AnimationCoordinator.IsRunning, Is.False);
            Assert.That(boot.LoadedProfile.LastSettledSpinId, Is.Null);
            Assert.That(boot.SaveRepository.Load().Data.PendingSpin.SpinId, Is.EqualTo(id));
            Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(20000));
            yield return Load();
            Assert.That(boot.LoadedProfile.FreeSpinsRemaining, Is.EqualTo(2));
            Assert.That(boot.LoadedProfile.BonusBet, Is.EqualTo(800));
            Assert.That(boot.LoadedProfile.PendingSpin, Is.Null);
            Assert.That(boot.LoadedProfile.LastWin, Is.Zero);
            Assert.That(boot.LoadedProfile.JackpotWinsThisWeek, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ReinitializingViewDoesNotDuplicateButtonOrAnimationSubscriptions()
        {
            yield return Load(); yield return Click(nav.PlayButton);
            var visual = AssetDatabase.LoadAssetAtPath<SlotVisualConfigSO>("Assets/_Project/Configs/SlotVisualConfigSO.asset");
            slot.Initialize(boot.RuntimeConfig, new SlotVisualSettings(visual.Resolve, visual.SpinDurationSeconds,
                visual.ReelStopStaggerSeconds, visual.ReelCellsPerSecond, visual.ReelStopEasePower));
            int requests = 0, completions = 0;
            slot.SpinRequested += () => requests++;
            slot.AnimationFinished += (_, success) => { if (success) completions++; };
            yield return Click(slot.SpinButton); yield return Finish();
            Assert.That(requests, Is.EqualTo(1)); Assert.That(completions, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator MissingUiReferenceDisablesMenuWithoutNullReferenceCascade()
        {
            yield return Load(); Object.Destroy(boot.gameObject); yield return null;
            var host = new GameObject("InvalidBootstrap"); host.SetActive(false);
            var invalid = host.AddComponent<SlotBootstrapper>();
            var serialized = new SerializedObject(invalid); serialized.FindProperty("navigationView").objectReferenceValue = nav;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            LogAssert.Expect(LogType.Error, "SlotBootstrapper initialization failed: Missing required UI references.");
            host.SetActive(true); yield return null;
            Assert.That(invalid.IsInitialized, Is.False);
            Assert.That(nav.PlayButton.interactable || nav.SettingsButton.interactable || nav.DailyBonusButton.interactable, Is.False);
            Assert.That(nav.transform.Find("MainMenuScreen/MenuStatus").GetComponent<Text>().text, Does.Contain("Unable to initialize"));
            Assert.That(slot.gameObject.activeInHierarchy, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator InvalidConfigurationAndMissingVisualAssetBlockStartupBeforePresentersConnect()
        {
            for (int scenario = 0; scenario < 2; scenario++)
            {
                yield return Load(); Object.Destroy(boot.gameObject); yield return null;
                var host = new GameObject("InvalidConfigBootstrap"); host.SetActive(false);
                var invalid = host.AddComponent<SlotBootstrapper>(); var serialized = new SerializedObject(invalid);
                var game = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset"));
                var visual = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SlotVisualConfigSO>("Assets/_Project/Configs/SlotVisualConfigSO.asset"));
                if (scenario == 0) game.Grid.ReelCount = 4; else visual.Symbols[0].Sprite = null;
                serialized.FindProperty("navigationView").objectReferenceValue = nav;
                serialized.FindProperty("slotView").objectReferenceValue = slot;
                serialized.FindProperty("jackpotView").objectReferenceValue = slot.GetComponentInChildren<JackpotView>(true);
                serialized.FindProperty("gameConfig").objectReferenceValue = game;
                serialized.FindProperty("visualConfig").objectReferenceValue = visual;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SlotBootstrapper initialization failed:"));
                host.SetActive(true); yield return null;
                Assert.That(invalid.IsInitialized, Is.False); Assert.That(invalid.Session, Is.Null);
                Assert.That(invalid.SlotPresenter, Is.Null); Assert.That(invalid.JackpotPresenter, Is.Null);
                Assert.That(nav.PlayButton.interactable, Is.False); Assert.That(invalid.InitializationError, Is.Not.Empty);
                Object.Destroy(game); Object.Destroy(visual); LogAssert.NoUnexpectedReceived();
            }
        }
        [UnityTest] public IEnumerator CorruptPrimaryRestoresBackupAndUnsupportedVersionNeverResetsProfile()
        {
            yield return Load();
            var profile = boot.LoadedProfile; profile.Balance = 12345;
            Assert.That(boot.SaveRepository.Save(profile).Success, Is.True);
            profile.Balance = 12545; Assert.That(boot.SaveRepository.Save(profile).Success, Is.True);
            File.WriteAllText(Path.Combine(directory, "save.json"), "{broken");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Recovered save.json.bak"));
            yield return Load(); Assert.That(boot.LoadedProfile.Balance, Is.EqualTo(12345));
            string unsupported = File.ReadAllText(Path.Combine(directory, "save.json.bak")).Replace("\"SaveVersion\": 1", "\"SaveVersion\": 99");
            // Codec currently writes compact JSON; match both whitespace conventions.
            unsupported = unsupported.Replace("\"SaveVersion\":1", "\"SaveVersion\":99");
            Assert.That(unsupported, Does.Contain("99")); File.WriteAllText(Path.Combine(directory, "save.json"), unsupported);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SlotBootstrapper initialization failed:"));
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single)); yield return null;
            boot = Object.FindFirstObjectByType<SlotBootstrapper>(); nav = Object.FindFirstObjectByType<GameNavigationView>();
            Assert.That(boot.IsInitialized, Is.False); Assert.That(nav.PlayButton.interactable, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(directory, "save.json")), Is.EqualTo(unsupported));
            LogAssert.NoUnexpectedReceived();
        }
        private void SetResolution(int width, int height)
        {
            var assembly = typeof(UnityEditor.Editor).Assembly;
            if (!gameView)
            {
                gameView = EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
                originalSize = (int)gameView.GetType().GetProperty("selectedSizeIndex", Instance).GetValue(gameView);
                var type = assembly.GetType("UnityEditor.GameViewSizes");
                var sizes = type.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                sizeGroup = type.GetProperty("currentGroup", Instance).GetValue(sizes);
            }
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"),
                new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "IceP 4.13 QA" });
            sizeGroup.GetType().GetMethod("AddCustomSize", Instance).Invoke(sizeGroup, new[] { size }); addedSizes++;
            int index = (int)sizeGroup.GetType().GetMethod("GetTotalCount", Instance).Invoke(sizeGroup, null) - 1;
            gameView.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(gameView, index);
            gameView.Focus(); gameView.Repaint();
        }
        private static void AuditVisibleControls()
        {
            Canvas.ForceUpdateCanvases(); var viewport = new Rect(0, 0, Screen.width, Screen.height);
            foreach (var control in Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None))
            {
                if (!control.gameObject.activeInHierarchy) continue;
                var bounds = Bounds((RectTransform)control.transform);
                Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(viewport.xMin), control.name);
                Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(viewport.yMin), control.name);
                Assert.That(bounds.xMax, Is.LessThanOrEqualTo(viewport.xMax), control.name);
                Assert.That(bounds.yMax, Is.LessThanOrEqualTo(viewport.yMax), control.name);
            }
            foreach (var label in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                var bounds = Bounds(label.rectTransform);
                Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(-1), label.name);
                Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(-1), label.name);
                Assert.That(bounds.xMax, Is.LessThanOrEqualTo(Screen.width + 1), label.name);
                Assert.That(bounds.yMax, Is.LessThanOrEqualTo(Screen.height + 1), label.name);
            }
        }
        private IEnumerator Capture(string name)
        {
            gameView.Focus(); gameView.Repaint();
            yield return new WaitForSecondsRealtime(.2f); Canvas.ForceUpdateCanvases();
            AuditVisibleControls();
            var path = Path.Combine(captureDirectory, name + ".png"); Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Unity queues a native capture at the end of a normal frame. Never call Camera.Render,
            // recursive PlayerLoop, or the screenshot plugin while executing an MCP request.
            bool rendered = false;
            for (int attempt = 0; attempt < 3 && !rendered; attempt++)
            {
                if (File.Exists(path)) File.Delete(path); // This test's own capture only.
                gameView.Repaint(); ScreenCapture.CaptureScreenshot(path);
                yield return new WaitForSecondsRealtime(.2f);
                float deadline = Time.realtimeSinceStartup + 5;
                while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(File.Exists(path), Is.True, path);
                var texture = new Texture2D(2, 2);
                try
                {
                    Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True);
                    Assert.That(texture.width, Is.EqualTo(Screen.width)); Assert.That(texture.height, Is.EqualTo(Screen.height));
                    for (int x = 1; x < 10; x++) for (int y = 1; y < 10; y++)
                    {
                        var pixel = texture.GetPixel(texture.width * x / 10, texture.height * y / 10);
                        if (Mathf.Max(pixel.r, pixel.g, pixel.b) > .02f) rendered = true;
                    }
                }
                finally { Object.Destroy(texture); }
                if (!rendered) yield return new WaitForSecondsRealtime(.5f);
            }
            Assert.That(rendered, Is.True, "Native screenshot was blank: " + path);
        }
        [UnityTest] public IEnumerator RenderedAspectRatiosKeepControlsAndExactLargeAmountsReadable()
        {
            yield return Load();
            foreach (var resolution in new[] { new Vector2Int(1920, 1080), new Vector2Int(1600, 1200), new Vector2Int(2340, 1080), new Vector2Int(1920, 1200), new Vector2Int(2436, 1125) })
            {
                SetResolution(resolution.x, resolution.y); yield return null; yield return null;
                string tag = resolution.x + "x" + resolution.y;
                Assert.That(Screen.width, Is.EqualTo(resolution.x)); Assert.That(Screen.height, Is.EqualTo(resolution.y));
                yield return Capture(tag + "-menu"); yield return Click(nav.SettingsButton); yield return Capture(tag + "-settings");
                yield return Click(nav.CloseSettingsButton); yield return Click(nav.DailyBonusButton); yield return Capture(tag + "-daily");
                Assert.That(nav.DailyBonusView.RewardLabels.Length, Is.EqualTo(7)); yield return Click(nav.CloseDailyBonusButton); yield return Click(nav.PlayButton);
                boot.enabled = false; // Visual projection fixtures never alter saved monetary values.
                long[] balances = { 20000, 119000, 999999, 123456789, long.MaxValue };
                long[] wins = { 0, 100000, 850000, 1234567, long.MaxValue };
                for (int i = 0; i < balances.Length; i++)
                {
                    var profile = boot.LoadedProfile; profile.Balance = balances[i]; profile.LastWin = wins[i];
                    var projection = new GameSession(profile);
                    slot.Render(new SlotDisplay(projection, new GameNavigationService(projection), false));
                    yield return Capture(tag + "-slot-" + i);
                    var balance = slot.transform.Find("SlotHUD/Balance").GetComponent<Text>();
                    var win = slot.transform.Find("SlotHUD/LastWin").GetComponent<Text>();
                    Assert.That(Bounds(balance.rectTransform).Overlaps(Bounds(win.rectTransform)), Is.False);
                    foreach (var label in new[] { balance, win })
                    {
                        Assert.That(label.cachedTextGenerator.characterCountVisible, Is.GreaterThanOrEqualTo(label.text.Length), label.text);
                        Assert.That(label.cachedTextGenerator.fontSizeUsedForBestFit, Is.GreaterThanOrEqualTo(Mathf.FloorToInt(32 * label.pixelsPerUnit)));
                        Assert.That(Bounds(label.rectTransform).Overlaps(Bounds((RectTransform)slot.transform.Find("JackpotHUD"))), Is.False);
                    }
                }
                boot.enabled = true; boot.SlotPresenter.Refresh(); yield return Click(slot.BackButton);
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
