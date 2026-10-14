using TMPro;
using System.Linq;
using IceP.Bootstrap;
using IceP.Domain.Configuration;
using IceP.Infrastructure.Configuration;
using IceP.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Editor
{
    public static class SlotUiAuthoring
    {
        private static TMP_FontAsset font;
        private static Sprite buttonSprite;
        private static Color buttonColor;
        [MenuItem("IceP/Author section 4.10 UI")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/SC.unity");
            var canvas = scene.GetRootGameObjects().Single(x => x.name == "Canvas");
            var slot = canvas.transform.Find("GameScreen");
            if (slot.GetComponent<SlotView>())
            {
                StyleControls(slot);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); return;
            }
            var nav = canvas.GetComponent<GameNavigationView>();
            font = nav.CloseSettingsButton.GetComponentInChildren<TMP_Text>(true).font;
            buttonSprite = nav.PlayButton.GetComponent<Image>().sprite;
            buttonColor = nav.PlayButton.GetComponent<Image>().color;
            var visual = AssetDatabase.LoadAssetAtPath<SlotVisualConfigSO>("Assets/_Project/Configs/SlotVisualConfigSO.asset");
            var game = AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset");
            var config = new LocalSlotConfigProvider(game).Load();
            var field = slot.Find("ImageGameField");
            var wheels = (RectTransform)field.Find("SlotWheels");
            wheels.anchoredPosition = Vector2.zero; wheels.sizeDelta = new Vector2(1200, 660);
            var coordinator = wheels.gameObject.AddComponent<ReelAnimationCoordinator>();
            var co = new SerializedObject(coordinator); var refs = co.FindProperty("reels"); refs.arraySize = 5;
            for (int reel = 0; reel < 5; reel++)
            {
                var wheel = (RectTransform)wheels.GetChild(reel);
                wheel.sizeDelta = new Vector2(220, 660); wheel.anchoredPosition = new Vector2((reel - 2) * 250, 0);
                wheel.gameObject.AddComponent<RectMask2D>();
                var images = wheel.GetComponentsInChildren<Image>(true).ToList();
                for (int i = 0; i < 2; i++) images.Add(Rect(wheel, "ScrollBuffer" + i, 0, 0, 200, 200).gameObject.AddComponent<Image>());
                var rv = wheel.gameObject.AddComponent<ReelView>(); var so = new SerializedObject(rv);
                var cells = so.FindProperty("cells"); cells.arraySize = 5;
                for (int i = 0; i < 5; i++)
                {
                    var image = images[i]; image.preserveAspect = true; image.raycastTarget = false;
                    image.rectTransform.sizeDelta = new Vector2(200, 200);
                    int row = i < 3 ? i : i == 3 ? -1 : 3;
                    image.rectTransform.anchoredPosition = new Vector2(0, (1 - row) * 220);
                    int position = (row + 30) % 30;
                    image.sprite = visual.Resolve(config.ReelStrips[reel].Symbols[position]);
                    cells.GetArrayElementAtIndex(i).objectReferenceValue = image;
                }
                so.ApplyModifiedPropertiesWithoutUndo(); refs.GetArrayElementAtIndex(reel).objectReferenceValue = rv;
            }
            co.ApplyModifiedPropertiesWithoutUndo();
            var line = Rect(field, "WinningPaylines", 0, 0, 1250, 660).gameObject.AddComponent<PaylineView>();
            line.raycastTarget = false;
            var view = slot.gameObject.AddComponent<SlotView>(); var vs = new SerializedObject(view);
            Set(vs, "animationCoordinator", coordinator); Set(vs, "paylines", line); Set(vs, "backButton", nav.BackButton);
            var hud = Rect(slot, "SlotHUD", 0, 475, 1350, 150);
            Set(vs, "balanceLabel", Label(hud, "Balance", "Coins: 20,000", -420, 30, 420, 52, 32));
            Set(vs, "winLabel", Label(hud, "LastWin", "Last win: 0", 20, 30, 420, 52, 32));
            Set(vs, "freeSpinsLabel", Label(hud, "FreeSpins", "Free Spins: 0", 475, 30, 400, 52, 30));
            var controls = Rect(slot, "SlotControls", 0, -460, 1400, 170);
            Set(vs, "betLabel", Label(controls, "Bet", "Bet: 200", -375, 35, 300, 60, 30));
            Set(vs, "betDownButton", Button(controls, "BetDecrease", "−", -580, 35, 90));
            Set(vs, "betUpButton", Button(controls, "BetIncrease", "+", -175, 35, 90));
            Set(vs, "spinButton", Button(controls, "SpinButton", "SPIN", 130, 35, 340));
            var retry = Button(controls, "RetrySpinSave", "Retry save", 510, 35, 290);
            Set(vs, "retryButton", retry); retry.gameObject.SetActive(false);
            vs.ApplyModifiedPropertiesWithoutUndo();
            StyleControls(slot);
            var boot = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<SlotBootstrapper>(true)).Single();
            var bs = new SerializedObject(boot); Set(bs, "slotView", view); bs.ApplyModifiedPropertiesWithoutUndo();
            // Keep existing ice artwork and all existing object/asset identities.
            foreach (var image in field.GetComponentsInChildren<Image>(true)) image.raycastTarget = false;
            var colors = nav.BackButton.colors; colors.disabledColor = new Color(.4f, .4f, .4f, .5f); nav.BackButton.colors = colors;
            EditorUtility.SetDirty(visual); // Serialize new animation defaults alongside existing mappings.
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("Section 4.10 UI authored inside existing GameScreen.");
        }
        private static void Set(SerializedObject obj, string name, Object value) => obj.FindProperty(name).objectReferenceValue = value;
        private static void StyleControls(Transform slot)
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/UiAtlas.png").OfType<Sprite>().ToArray();
            var controls = (RectTransform)slot.Find("SlotControls"); controls.anchoredPosition = new Vector2(0, -460);
            foreach (var button in controls.GetComponentsInChildren<Button>(true))
            {
                bool icon = button.name == "BetIncrease" || button.name == "BetDecrease";
                string spriteName = button.name == "BetIncrease" ? "Button_Plus_Round_Blue" :
                    button.name == "BetDecrease" ? "Button_Minus_Round_Blue" : "Button_Pill_Blue";
                var image = button.GetComponent<Image>(); image.sprite = sprites.Single(x => x.name == spriteName);
                image.color = Color.white; image.type = icon ? Image.Type.Simple : Image.Type.Sliced; image.preserveAspect = icon;
                if (icon) button.GetComponentInChildren<TMP_Text>(true).text = "";
            }
            var line = slot.Find("ImageGameField/WinningPaylines");
            if (!line.GetComponent<CanvasRenderer>()) line.gameObject.AddComponent<CanvasRenderer>();
        }
        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }
        private static TMP_Text Label(Transform parent, string name, string text, float x, float y, float w, float h, int size)
        {
            var label = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>(); label.font = font;
            label.text = text; label.fontSize = size; label.alignment = TextAlignmentOptions.Center; label.color = Color.white;
            label.raycastTarget = false; label.gameObject.AddComponent<Shadow>().effectColor = new Color(0, .1f, .2f, .9f); return label;
        }
        private static Button Button(Transform parent, string name, string text, float x, float y, float width)
        {
            var image = Rect(parent, name, x, y, width, 75).gameObject.AddComponent<Image>();
            image.sprite = buttonSprite; image.color = buttonColor; image.type = Image.Type.Sliced;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.disabledColor = new Color(.4f, .4f, .4f, .5f); button.colors = colors;
            Label(image.transform, "Label", text, 0, 0, width - 10, 65, 30); return button;
        }
    }
}
