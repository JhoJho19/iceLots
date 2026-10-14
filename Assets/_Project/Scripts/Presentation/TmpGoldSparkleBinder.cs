using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Presentation
{
    /// <summary>Owns per-label bounds and a procedural flare without changing TMP's glyph mesh.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class TmpGoldSparkleBinder : MonoBehaviour
    {
        [SerializeField, Tooltip("A gold material preset made from this text's font asset.")]
        private Material materialPreset;
        private TextMeshProUGUI label;
        private Material original, textMaterial, sparkleMaterial;
        private TmpGoldSparkleGraphic sparkle;
        private Vector4 bounds;
        private float renderedSize;
        private bool hasVisibleText;
        private static readonly int BoundsId = Shader.PropertyToID("_TextBounds");
        private static readonly string[] EffectColors = { "_GoldColorA", "_GoldColorB", "_ShineColor", "_SparkleColor" };
        private static readonly string[] EffectFloats = { "_ShineIntensity", "_ShineSpeed", "_ShineWidth", "_SparkleIntensity",
            "_SparkleSize", "_SparklePulseSpeed", "_SparkleOffsetX", "_SparkleOffsetY" };

        public Material MaterialPreset
        {
            get => materialPreset;
            set { if (materialPreset == value) return; Release(); materialPreset = value; if (UnityEngine.Application.isPlaying && isActiveAndEnabled) Bind(); }
        }
        public Vector4 BoundTextRect => bounds;
        public RectTransform SparkleRect => sparkle ? sparkle.rectTransform : null;

        private void OnEnable() => Bind();
        private void Bind()
        {
            if (!UnityEngine.Application.isPlaying) return;
            label = GetComponent<TextMeshProUGUI>();
            var preset = materialPreset ? materialPreset : label.fontSharedMaterial;
            if (!preset || !preset.HasProperty(BoundsId) || !preset.HasProperty("_IsSparkle")) return;
            original = label.fontSharedMaterial;
            textMaterial = new Material(preset) { name = preset.name + " (Text Instance)", hideFlags = HideFlags.HideAndDontSave };
            sparkleMaterial = new Material(preset) { name = preset.name + " (Sparkle Instance)", hideFlags = HideFlags.HideAndDontSave };
            textMaterial.SetFloat("_IsSparkle", 0);
            sparkleMaterial.SetFloat("_IsSparkle", 1);
            label.fontSharedMaterial = textMaterial;
            var child = new GameObject("Procedural Gold Sparkle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TmpGoldSparkleGraphic));
            child.hideFlags = HideFlags.HideAndDontSave;
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            sparkle = child.GetComponent<TmpGoldSparkleGraphic>();
            sparkle.raycastTarget = false;
            sparkle.material = sparkleMaterial;
            var rect = sparkle.rectTransform;
            rect.anchorMin = rect.anchorMax = label.rectTransform.pivot;
            rect.pivot = new Vector2(.5f, .5f);
            label.OnPreRenderText += UpdateBounds;
            label.SetVerticesDirty();
        }

        private void UpdateBounds(TMP_TextInfo info)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            hasVisibleText = false;
            for (int i = 0; i < info.characterCount; i++)
            {
                var character = info.characterInfo[i];
                if (!character.isVisible) continue;
                min = Vector2.Min(min, character.bottomLeft);
                max = Vector2.Max(max, character.topRight);
                hasVisibleText = true;
            }
            if (hasVisibleText)
            {
                bounds = new Vector4(min.x, min.y, Mathf.Max(1, max.x - min.x), Mathf.Max(1, max.y - min.y));
                textMaterial.SetVector(BoundsId, bounds);
                label.materialForRendering.SetVector(BoundsId, bounds);
            }
            renderedSize = label.fontSize;
            // TMP invokes this during Canvas rebuilds. A child Graphic must be dirtied
            // outside that loop; LateUpdate applies the new geometry on the next frame.
            if (!CanvasUpdateRegistry.IsRebuildingGraphics()) UpdateSparkle();
        }

        // The shader animates; the component only synchronizes preset settings and geometry.
        private void LateUpdate()
        {
            if (!textMaterial) return;
            if (materialPreset)
            {
                textMaterial.CopyPropertiesFromMaterial(materialPreset);
                textMaterial.SetFloat("_IsSparkle", 0);
                textMaterial.SetVector(BoundsId, bounds);
                sparkleMaterial.CopyPropertiesFromMaterial(materialPreset);
                sparkleMaterial.SetFloat("_IsSparkle", 1);
            }
            UpdateSparkle();
            // Unity caches a separate stencil material under Mask. Keep custom values
            // current without overwriting that material's stencil or clipping properties.
            SyncMaskedMaterial(label.materialForRendering, textMaterial);
            if (sparkle) SyncMaskedMaterial(sparkle.materialForRendering, sparkleMaterial);
        }

        private void SyncMaskedMaterial(Material rendered, Material source)
        {
            if (!rendered || rendered == source) return;
            foreach (var property in EffectColors) rendered.SetColor(property, source.GetColor(property));
            foreach (var property in EffectFloats) rendered.SetFloat(property, source.GetFloat(property));
            rendered.SetVector(BoundsId, bounds);
        }

        private void UpdateSparkle()
        {
            if (!sparkle) return;
            float radius = Mathf.Max(0, textMaterial.GetFloat("_SparkleSize")) * renderedSize;
            bool visible = hasVisibleText && !string.IsNullOrEmpty(label.text) && radius > .001f && textMaterial.GetFloat("_SparkleIntensity") > 0;
            if (sparkle.gameObject.activeSelf != visible) sparkle.gameObject.SetActive(visible);
            if (!visible) return;
            var pivot = label.rectTransform.pivot;
            if (sparkle.rectTransform.anchorMin != pivot || sparkle.rectTransform.anchorMax != pivot)
                sparkle.rectTransform.anchorMin = sparkle.rectTransform.anchorMax = pivot;
            var size = Vector2.one * (radius * 2);
            if (sparkle.rectTransform.sizeDelta != size) sparkle.rectTransform.sizeDelta = size;
            var position = new Vector2(
                bounds.x + bounds.z + textMaterial.GetFloat("_SparkleOffsetX") * renderedSize,
                bounds.y + bounds.w + textMaterial.GetFloat("_SparkleOffsetY") * renderedSize);
            if (sparkle.rectTransform.anchoredPosition != position) sparkle.rectTransform.anchoredPosition = position;
            sparkle.color = new Color(1, 1, 1, label.color.a);
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();
        private void Release()
        {
            if (label)
            {
                label.OnPreRenderText -= UpdateBounds;
                if (textMaterial && label.fontSharedMaterial == textMaterial) label.fontSharedMaterial = original;
            }
            if (sparkle) Dispose(sparkle.gameObject);
            if (textMaterial) Dispose(textMaterial);
            if (sparkleMaterial) Dispose(sparkleMaterial);
            sparkle = null; textMaterial = null; sparkleMaterial = null;
            hasVisibleText = false;
        }
        private static void Dispose(Object value)
        {
            if (UnityEngine.Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
