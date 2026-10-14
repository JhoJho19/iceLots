using TMPro;
using System.Linq;
using IceP.Bootstrap;
using IceP.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Editor
{
    public static class JackpotUiAuthoring
    {
        [MenuItem("IceP/Author section 4.11 JackpotHUD")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/SC.unity");
            var boot = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<SlotBootstrapper>(true)).Single();
            var slot = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<SlotView>(true)).Single();
            var hud = slot.transform.Find("JackpotHUD");
            JackpotView view;
            if (!hud)
            {
                var rect = Rect(slot.transform, "JackpotHUD", 0, 400, 1250, 110);
                var image = rect.gameObject.AddComponent<Image>();
                image.color = new Color(.025f, .1f, .18f, .92f); image.raycastTarget = false;
                var outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(.8f, .65f, .25f, .7f);
                view = rect.gameObject.AddComponent<JackpotView>();
                var font = slot.GetComponentsInChildren<TMP_Text>(true).First().font;
                var so = new SerializedObject(view);
                Set(so, "background", image);
                Set(so, "titleLabel", Label(rect, font, "Title", "JACKPOT OF THE WEEK", -420, 27, 380, 36, 23, Color.white));
                Set(so, "amountLabel", Label(rect, font, "Amount", "", -420, -17, 380, 44, 32, new Color(1, .83f, .35f)));
                Set(so, "availabilityLabel", Label(rect, font, "Availability", "", 0, -22, 340, 35, 17, Color.white));
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else view = hud.GetComponent<JackpotView>();
            // Keep large configurable amounts on one line within the existing compact banner.
            var amount = view.transform.Find("Amount").GetComponent<TMP_Text>();
            amount.enableAutoSizing = true; amount.fontSizeMin = 12; amount.fontSizeMax = 32;
            var bs = new SerializedObject(boot); Set(bs, "jackpotView", view); bs.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        private static void Set(SerializedObject so, string name, Object value) => so.FindProperty(name).objectReferenceValue = value;
        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, y); return rect;
        }
        private static TMP_Text Label(Transform parent, TMP_FontAsset font, string name, string text, float x, float y, float w, float h, int size, Color color)
        {
            var label = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = text; label.fontSize = size; label.color = color;
            label.enableAutoSizing = true; label.fontSizeMin = size * .75f; label.fontSizeMax = size;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            return label;
        }
    }
}
