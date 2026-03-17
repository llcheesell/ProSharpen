#if PROSHARPEN_HDRP
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using LLCheesell.ProSharpen;
using static LLCheesell.ProSharpen.Editor.ProSharpenEditorCommon;
using static LLCheesell.ProSharpen.Editor.ProSharpenEditorCommon.Styles;

namespace LLCheesell.ProSharpen.Editor
{
    [CustomEditor(typeof(ProSharpenVolume))]
    sealed class ProSharpenVolumeEditor : VolumeComponentEditor
    {
        // Core
        SerializedDataParameter _enabled;
        SerializedDataParameter _mode;
        SerializedDataParameter _quality;
        SerializedDataParameter _intensity;
        SerializedDataParameter _sharpness;

        // Advanced
        SerializedDataParameter _lumaOnly;
        SerializedDataParameter _edgeProtection;
        SerializedDataParameter _detailBoost;
        SerializedDataParameter _noiseThreshold;
        SerializedDataParameter _clampLimit;
        SerializedDataParameter _cornerWeight;

        // Depth-Aware
        SerializedDataParameter _depthAware;
        SerializedDataParameter _depthNearStrength;
        SerializedDataParameter _depthFarStrength;
        SerializedDataParameter _depthNearPlane;
        SerializedDataParameter _depthFarPlane;

        // Motion-Aware
        SerializedDataParameter _motionAware;
        SerializedDataParameter _motionThreshold;
        SerializedDataParameter _motionReduction;

        // Mask
        SerializedDataParameter _maskEnabled;
        SerializedDataParameter _maskTexture;

        public override void OnEnable()
        {
            base.OnEnable();

            var o = new PropertyFetcher<ProSharpenVolume>(serializedObject);

            _enabled = Unpack(o.Find("enabled"));
            _mode = Unpack(o.Find("mode"));
            _quality = Unpack(o.Find("quality"));
            _intensity = Unpack(o.Find("intensity"));
            _sharpness = Unpack(o.Find("sharpness"));

            _lumaOnly = Unpack(o.Find("lumaOnly"));
            _edgeProtection = Unpack(o.Find("edgeProtection"));
            _detailBoost = Unpack(o.Find("detailBoost"));
            _noiseThreshold = Unpack(o.Find("noiseThreshold"));
            _clampLimit = Unpack(o.Find("clampLimit"));
            _cornerWeight = Unpack(o.Find("cornerWeight"));

            _depthAware = Unpack(o.Find("depthAware"));
            _depthNearStrength = Unpack(o.Find("depthNearStrength"));
            _depthFarStrength = Unpack(o.Find("depthFarStrength"));
            _depthNearPlane = Unpack(o.Find("depthNearPlane"));
            _depthFarPlane = Unpack(o.Find("depthFarPlane"));

            _motionAware = Unpack(o.Find("motionAware"));
            _motionThreshold = Unpack(o.Find("motionThreshold"));
            _motionReduction = Unpack(o.Find("motionReduction"));

            _maskEnabled = Unpack(o.Find("maskEnabled"));
            _maskTexture = Unpack(o.Find("maskTexture"));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(_enabled);
            PropertyField(_mode, ModeLabel);
            PropertyField(_quality, QualityLabel);
            PropertyField(_intensity, IntensityLabel);
            PropertyField(_sharpness, SharpnessLabel);

            var mode = (SharpenMode)_mode.value.intValue;
            var quality = (SharpenQuality)_quality.value.intValue;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);

            PropertyField(_lumaOnly, LumaOnlyLabel);

            if (IsEdgeProtectionVisible(mode))
                PropertyField(_edgeProtection, EdgeProtectionLabel);

            if (IsDetailBoostVisible(mode))
                PropertyField(_detailBoost, DetailBoostLabel);

            if (IsNoiseThresholdVisible(mode))
                PropertyField(_noiseThreshold, NoiseThresholdLabel);

            PropertyField(_clampLimit, ClampLimitLabel);

            if (IsCornerWeightVisible(mode, quality))
                PropertyField(_cornerWeight, CornerWeightLabel);

            // Depth-Aware
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Depth-Aware", EditorStyles.boldLabel);
            PropertyField(_depthAware, DepthAwareLabel);

            if (_depthAware.value.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(_depthNearStrength, DepthNearStrengthLabel);
                    PropertyField(_depthFarStrength, DepthFarStrengthLabel);
                    PropertyField(_depthNearPlane);
                    PropertyField(_depthFarPlane);
                }
            }

            // Motion-Aware
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("TAA/DLSS Recovery", EditorStyles.boldLabel);
            PropertyField(_motionAware, MotionAwareLabel);

            if (_motionAware.value.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(_motionThreshold, MotionThresholdLabel);
                    PropertyField(_motionReduction, MotionReductionLabel);
                }
            }

            // Masking
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selective Masking", EditorStyles.boldLabel);
            PropertyField(_maskEnabled, MaskEnabledLabel);

            if (_maskEnabled.value.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(_maskTexture, MaskTextureLabel);
                }
            }
        }
    }
}
#endif
