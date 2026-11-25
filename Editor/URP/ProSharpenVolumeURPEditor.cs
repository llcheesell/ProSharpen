#if PROSHARPEN_URP
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using LLCheesell.ProSharpen;
using static LLCheesell.ProSharpen.Editor.ProSharpenEditorCommon;
using static LLCheesell.ProSharpen.Editor.ProSharpenEditorCommon.Styles;

namespace LLCheesell.ProSharpen.Editor
{
    [CustomEditor(typeof(ProSharpenVolumeURP))]
    sealed class ProSharpenVolumeURPEditor : VolumeComponentEditor
    {
        // Core
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
        SerializedDataParameter _radius;

        // Halo Control
        SerializedDataParameter _overshoot;
        SerializedDataParameter _undershoot;

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

            var o = new PropertyFetcher<ProSharpenVolumeURP>(serializedObject);

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
            _radius = Unpack(o.Find("radius"));

            _overshoot = Unpack(o.Find("overshoot"));
            _undershoot = Unpack(o.Find("undershoot"));

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

            if (IsClampLimitVisible(mode))
                PropertyField(_clampLimit, ClampLimitLabel);

            if (IsCornerWeightVisible(mode, quality))
                PropertyField(_cornerWeight, CornerWeightLabel);

            if (IsRadiusVisible(mode))
                PropertyField(_radius, RadiusLabel);

            // Halo Control
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Halo Control", EditorStyles.boldLabel);
            PropertyField(_overshoot, OvershootLabel);
            PropertyField(_undershoot, UndershootLabel);

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

            // モーション連動 (TAA/DLSS Recovery) は URP では未対応のため表示しない

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
