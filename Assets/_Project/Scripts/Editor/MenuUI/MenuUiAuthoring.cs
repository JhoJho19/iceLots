using TMPro;
using System.Linq;
using IceP.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Editor
{
    // Explicit editor-only authoring command. Runtime uses serialized references exclusively.
    public static class MenuUiAuthoring
    {
        private static TMP_FontAsset font;
        private static Sprite buttonSprite;
        private static Color buttonColor;
        [MenuItem("IceP/Author section 4.9 UI")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/SC.unity");
            var nav = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<GameNavigationView>(true)).Single();
            if (nav.SettingsView) return; // Do not duplicate authored controls.
            var serialized = new SerializedObject(nav);
            var menu = Ref<GameObject>(serialized, "mainMenuScreen").transform;
            var settings = Ref<GameObject>(serialized, "settingsPopup");
            var daily = Ref<GameObject>(serialized, "dailyBonusPopup");
            font = nav.CloseSettingsButton.GetComponentInChildren<TMP_Text>(true).font;
            buttonSprite = nav.PlayButton.GetComponent<Image>().sprite;
            buttonColor = nav.PlayButton.GetComponent<Image>().color;
            var settingsPanel = Panel(settings.transform, "AudioPreferences", 760, 650);
            var dailyPanel = Panel(daily.transform, "DailyRewards", 900, 730);
            MoveExisting(settings, "Title", settingsPanel, 265, 700, 60);
            MoveExisting(settings, "Placeholder", settingsPanel, -165, 690, 65);
            MoveExisting(daily, "Title", dailyPanel, 300, 800, 60);
            MoveExisting(daily, "Placeholder", dailyPanel, -190, 820, 70);
            MoveButton(nav.CloseSettingsButton, settingsPanel, -265);
            MoveButton(nav.CloseDailyBonusButton, dailyPanel, -310);
            var sv = settings.AddComponent<SettingsView>();
            var so = new SerializedObject(sv);
            Label(settingsPanel, "Music", "Music", -85, 155, 380, 50, 28);
            Set(so, "musicVolume", Slider(settingsPanel, "MusicVolume", 85));
            Set(so, "musicPercent", Label(settingsPanel, "MusicPercent", "100%", 270, 85, 100, 40, 24));
            Label(settingsPanel, "Sound Effects", "Sound Effects", -85, -5, 380, 50, 28);
            Set(so, "soundVolume", Slider(settingsPanel, "EffectsVolume", -75));
            Set(so, "soundPercent", Label(settingsPanel, "EffectsPercent", "100%", 270, -75, 100, 40, 24));
            var settingsMessage = settingsPanel.GetComponentsInChildren<TMP_Text>().Single(x => x.name == "Placeholder");
            settingsMessage.text = ""; settingsMessage.fontSize = 22;
            Set(so, "message", settingsMessage);
            ((RectTransform)nav.CloseSettingsButton.transform).anchoredPosition = new Vector2(-160, -265);
            so.ApplyModifiedPropertiesWithoutUndo();

            var dv = daily.AddComponent<DailyBonusView>();
            var ds = new SerializedObject(dv);
            Set(ds, "rewardDefaultSprite", AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Sprites/UiAtlas.png").OfType<Sprite>().Single(x => x.name == "Button_Hex_Blue"));
            Set(ds, "rewardClaimedSprite", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprites/Reward day claimed sprite.png"));
            var labels = ds.FindProperty("rewardLabels"); var panels = ds.FindProperty("rewardPanels");
            labels.arraySize = panels.arraySize = 7;
            for (int i = 0; i < 7; i++)
            {
                float x = i < 4 ? (i - 1.5f) * 205 : (i - 5) * 245;
                float y = i < 4 ? 150 : -10;
                var card = Image(dailyPanel, "RewardDay" + (i + 1), x, y, i == 6 ? 225 : 190, 140, new Color(.18f, .16f, .28f));
                labels.GetArrayElementAtIndex(i).objectReferenceValue = Label(card.transform, "Reward", "Day " + (i + 1), 0, 0, i == 6 ? 215 : 180, 130, 23);
                panels.GetArrayElementAtIndex(i).objectReferenceValue = card;
            }
            var dailyMessage = dailyPanel.GetComponentsInChildren<TMP_Text>().Single(x => x.name == "Placeholder");
            dailyMessage.text = ""; dailyMessage.fontSize = 23;
            Set(ds, "message", dailyMessage);
            Set(ds, "claimButton", Button(dailyPanel, "ClaimButton", "Claim", 0, -245, 280));
            var dailyRetry = Button(dailyPanel, "RetryDailyBonus", "Retry reward", 285, -245, 245);
            Set(ds, "retryButton", dailyRetry); ds.ApplyModifiedPropertiesWithoutUndo(); dailyRetry.gameObject.SetActive(false);
            Set(serialized, "settingsView", sv); Set(serialized, "dailyBonusView", dv);
            Set(serialized, "dailyBonusAvailableMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/MaterialsAndShaders/ShiningIce 1.mat"));
            var balance = Label(menu, "Balance", "Coins: 20,000", -350, 440, 430, 60, 30);
            var balanceRect = (RectTransform)balance.transform;
            balanceRect.anchorMin = balanceRect.anchorMax = new Vector2(1, 1);
            balanceRect.anchoredPosition = new Vector2(-250, -65);
            Set(serialized, "balanceLabel", balance);
            Set(serialized, "menuMessage", Label(menu, "MenuStatus", "", 0, -430, 900, 60, 24));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // Expand scaling ensures fixed popup content fits narrow and wide aspect ratios.
            nav.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("Section 4.9 UI authored with serialized references.");
        }
        private static T Ref<T>(SerializedObject obj, string name) where T : Object => (T)obj.FindProperty(name).objectReferenceValue;
        private static void Set(SerializedObject obj, string name, Object value) => obj.FindProperty(name).objectReferenceValue = value;
        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }
        private static Image Image(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>(); image.color = color; return image;
        }
        private static RectTransform Panel(Transform parent, string name, float w, float h) =>
            (RectTransform)Image(parent, name, 0, 0, w, h, new Color(.09f, .07f, .16f, .98f)).transform;
        private static TMP_Text Label(Transform parent, string name, string text, float x, float y, float w, float h, int size)
        {
            var label = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = size; label.alignment = TextAlignmentOptions.Center; label.color = Color.white;
            label.text = text; label.raycastTarget = false; return label;
        }
        private static Button Button(Transform parent, string name, string text, float x, float y, float w)
        {
            var image = Image(parent, name, x, y, w, 58, buttonColor); image.sprite = buttonSprite; image.type = UnityEngine.UI.Image.Type.Sliced;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            Label(image.transform, "Label", text, 0, 0, w - 16, 50, 25); return button;
        }
        private static Slider Slider(Transform parent, string name, float y)
        {
            var rect = Rect(parent, name, -40, y, 400, 42); var slider = rect.gameObject.AddComponent<Slider>();
            var bg = Image(rect, "Track", 0, 0, 400, 12, new Color(.35f, .28f, .45f));
            var area = Rect(rect, "HandleArea", 0, 0, 380, 42);
            var handle = Image(area, "Handle", 0, 0, 26, 40, new Color(1, .8f, .25f));
            slider.handleRect = (RectTransform)handle.transform; slider.targetGraphic = handle;
            slider.minValue = 0; slider.maxValue = 1; slider.value = 1; return slider;
        }
        private static void MoveExisting(GameObject popup, string name, Transform parent, float y, float w, float h)
        {
            var rect = (RectTransform)popup.transform.Find(name); rect.SetParent(parent, false);
            rect.anchoredPosition = new Vector2(0, y); rect.sizeDelta = new Vector2(w, h);
        }
        private static void MoveButton(Button button, Transform parent, float y)
        { button.transform.SetParent(parent, false); ((RectTransform)button.transform).anchoredPosition = new Vector2(0, y); }
    }
}
