#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LLCheesell.ProSharpen.Editor
{
    [CustomEditor(typeof(ProSharpenMaskGenerator))]
    sealed class ProSharpenMaskGeneratorEditor : UnityEditor.Editor
    {
        SerializedProperty _sharpenLayers;
        SerializedProperty _resolutionScale;
        SerializedProperty _blurIterations;

        void OnEnable()
        {
            _sharpenLayers = serializedObject.FindProperty("sharpenLayers");
            _resolutionScale = serializedObject.FindProperty("resolutionScale");
            _blurIterations = serializedObject.FindProperty("blurIterations");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_sharpenLayers, new GUIContent("Sharpen Layers",
                "Objects on these layers will be sharpened. Others will be excluded."));
            EditorGUILayout.PropertyField(_resolutionScale, new GUIContent("Resolution Scale",
                "Mask resolution relative to screen. Lower = faster, higher = more precise edges."));
            EditorGUILayout.PropertyField(_blurIterations, new GUIContent("Edge Feather",
                "Blur iterations for smoother mask transitions."));

            serializedObject.ApplyModifiedProperties();

            // Mask preview
            var generator = (ProSharpenMaskGenerator)target;
            var maskRT = generator.MaskTexture;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mask Preview", EditorStyles.boldLabel);

            if (maskRT != null)
            {
                EditorGUILayout.LabelField($"Size: {maskRT.width} x {maskRT.height}", EditorStyles.miniLabel);
                var rect = GUILayoutUtility.GetAspectRect((float)maskRT.width / maskRT.height);
                EditorGUI.DrawPreviewTexture(rect, maskRT);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Mask texture is not yet generated. Enter Play mode or enable the component at runtime to see the preview.",
                    MessageType.Info);
            }
        }
    }
}
#endif
