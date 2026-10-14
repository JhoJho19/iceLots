#if UNITY_EDITOR
using System.Collections;
using IceP.Presentation;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace IceP.Tests
{
    public sealed class TmpGoldSparkleTests
    {
        private GameObject canvasObject;
        private Material preset;
        private TMP_FontAsset font;

        [SetUp] public void Setup()
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/Baloo2-Bold SDF.asset");
            preset = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Fonts/Baloo2-Bold Gold.mat");
            canvasObject = new GameObject("Gold Test Canvas", typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }
        [TearDown] public void Cleanup() => Object.DestroyImmediate(canvasObject);

        private TextMeshProUGUI Label(string value)
        {
            var go = new GameObject("Coins", typeof(RectTransform));
            go.SetActive(false); go.transform.SetParent(canvasObject.transform, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSharedMaterial = preset; label.fontSize = 32;
            label.rectTransform.sizeDelta = new Vector2(600, 100);
            label.text = value;
            go.AddComponent<TmpGoldSparkleBinder>().MaterialPreset = preset;
            go.SetActive(true);
            return label;
        }

        [UnityTest] public IEnumerator BoundsFollowTextAlignmentAutosizingAndResizeWithoutChangingPreset()
        {
            var originalBounds = preset.GetVector("_TextBounds");
            var label = Label("Coins: 20,000");
            yield return null; label.ForceMeshUpdate();
            var binder = label.GetComponent<TmpGoldSparkleBinder>();
            var before = binder.BoundTextRect;
            AssertCorner(label, binder);
            label.text = "Coins: 9,223,372,036,854,775,807";
            label.alignment = TextAlignmentOptions.Right;
            label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = 32;
            label.rectTransform.sizeDelta = new Vector2(350, 90);
            yield return null; label.ForceMeshUpdate();
            Assert.That(binder.BoundTextRect, Is.Not.EqualTo(before));
            AssertCorner(label, binder);
            Assert.That(preset.GetVector("_TextBounds"), Is.EqualTo(originalBounds));
            Assert.That(label.fontSharedMaterial, Is.Not.SameAs(preset));
            Assert.That(label.isTextOverflowing, Is.False);
        }

        private void AssertCorner(TextMeshProUGUI label, TmpGoldSparkleBinder binder)
        {
            var rect = binder.BoundTextRect;
            var position = binder.SparkleRect.anchoredPosition;
            Assert.That(position.x, Is.EqualTo(rect.x + rect.z + preset.GetFloat("_SparkleOffsetX") * label.fontSize).Within(.01));
            Assert.That(position.y, Is.EqualTo(rect.y + rect.w + preset.GetFloat("_SparkleOffsetY") * label.fontSize).Within(.01));
            Assert.That(binder.SparkleRect.sizeDelta.x, Is.EqualTo(2 * preset.GetFloat("_SparkleSize") * label.fontSize).Within(.01));
        }

        [UnityTest] public IEnumerator EachLabelHasOneIndependentNonRaycastingSparkleAndReleasesItsMaterials()
        {
            var first = Label("Coins: 20,000"); var second = Label("100,000 COINS");
            yield return null; first.ForceMeshUpdate(); second.ForceMeshUpdate();
            Assert.That(first.fontSharedMaterial, Is.Not.SameAs(second.fontSharedMaterial));
            var binder = first.GetComponent<TmpGoldSparkleBinder>();
            var instance = first.fontSharedMaterial;
            Assert.That(binder.SparkleRect.GetComponent<TmpGoldSparkleGraphic>().raycastTarget, Is.False);
            binder.enabled = false; yield return null;
            Assert.That(first.fontSharedMaterial, Is.SameAs(preset));
            Assert.That(instance == null, Is.True);
            binder.enabled = true; yield return null; first.ForceMeshUpdate();
            Assert.That(first.GetComponentsInChildren<TmpGoldSparkleGraphic>(true).Length, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator EmptyTextHidesSparkleAndCanvasMaskAndTintRemainSupported()
        {
            var mask = new GameObject("Mask", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            mask.transform.SetParent(canvasObject.transform, false);
            mask.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 100);
            var label = Label("Coins: 20,000"); label.transform.SetParent(mask.transform, false);
            label.color = new Color(1, 1, 1, .35f);
            yield return null; Canvas.ForceUpdateCanvases(); label.ForceMeshUpdate();
            var binder = label.GetComponent<TmpGoldSparkleBinder>();
            var graphic = binder.SparkleRect.GetComponent<TmpGoldSparkleGraphic>();
            Assert.That(graphic.color.a, Is.EqualTo(.35f).Within(.001));
            Assert.That(graphic.materialForRendering.GetFloat("_Stencil"), Is.GreaterThan(0));
            Assert.That(graphic.materialForRendering.GetFloat("_IsSparkle"), Is.EqualTo(1));
            Assert.That(label.materialForRendering.GetVector("_TextBounds"), Is.EqualTo(binder.BoundTextRect));
            label.text = ""; yield return null; label.ForceMeshUpdate();
            Assert.That(binder.SparkleRect.gameObject.activeSelf, Is.False);
        }
    }
}
#endif
