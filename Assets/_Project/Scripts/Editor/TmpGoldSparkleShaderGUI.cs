using TMPro.EditorUtilities;
using UnityEditor;
using UnityEngine;

namespace IceP.Editor
{
    public sealed class TmpGoldSparkleShaderGUI : TMP_SDFShaderGUI
    {
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            base.OnGUI(editor, properties);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Gold and procedural sparkle", EditorStyles.boldLabel);
            foreach (var name in new[] { "_GoldColorA", "_GoldColorB", "_ShineColor", "_ShineIntensity", "_ShineSpeed", "_ShineWidth",
                "_SparkleColor", "_SparkleIntensity", "_SparkleSize", "_SparklePulseSpeed", "_SparkleOffsetX", "_SparkleOffsetY" })
            {
                var property = FindProperty(name, properties);
                editor.ShaderProperty(property, property.displayName);
            }
            if (GUI.changed)
                foreach (var target in editor.targets) EditorUtility.SetDirty(target);
        }
    }
}
