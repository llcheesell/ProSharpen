#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LLCheesell.ProSharpen.Editor
{
    /// <summary>
    /// Shared editor utilities for ProSharpen inspectors.
    /// </summary>
    public static class ProSharpenEditorCommon
    {
        public static class Styles
        {
            public static readonly GUIContent ModeLabel = new GUIContent("Sharpen Mode",
                "CAS: Fast & artifact-resistant (AMD FidelityFX based)\n" +
                "Unsharp Mask: Classic photographic sharpening\n" +
                "Adaptive: Context-aware with edge protection");

            public static readonly GUIContent QualityLabel = new GUIContent("Quality",
                "Realtime: Optimized 3x3 kernel\nHigh: Extended neighborhood sampling");

            public static readonly GUIContent IntensityLabel = new GUIContent("Intensity",
                "Overall blend between original and sharpened image");

            public static readonly GUIContent SharpnessLabel = new GUIContent("Sharpness",
                "Strength of the sharpening effect");

            public static readonly GUIContent LumaOnlyLabel = new GUIContent("Luminance Only",
                "Process sharpening in luminance space to prevent color shifts");

            public static readonly GUIContent EdgeProtectionLabel = new GUIContent("Edge Protection",
                "Reduces sharpening on strong edges to prevent jaggies (Adaptive mode)");

            public static readonly GUIContent DetailBoostLabel = new GUIContent("Detail Boost",
                "Extra sharpening in low-contrast areas to recover fine detail (Adaptive mode)");

            public static readonly GUIContent NoiseThresholdLabel = new GUIContent("Noise Threshold",
                "Differences below this value are not sharpened. Prevents noise amplification (USM mode)");

            public static readonly GUIContent ClampLimitLabel = new GUIContent("Clamp Limit",
                "Maximum sharpening adjustment. Lower values prevent halos");

            public static readonly GUIContent CornerWeightLabel = new GUIContent("Corner Weight",
                "Weight of corner samples in CAS High Quality mode");

            // Depth-aware
            public static readonly GUIContent DepthAwareLabel = new GUIContent("Depth-Aware",
                "Modulate sharpening based on depth");

            public static readonly GUIContent DepthNearStrengthLabel = new GUIContent("Near Strength",
                "Sharpening multiplier for near objects");

            public static readonly GUIContent DepthFarStrengthLabel = new GUIContent("Far Strength",
                "Sharpening multiplier for far objects");

            // Motion-aware
            public static readonly GUIContent MotionAwareLabel = new GUIContent("TAA/DLSS Recovery",
                "Sharpen static areas more to recover softness from temporal anti-aliasing");

            public static readonly GUIContent MotionThresholdLabel = new GUIContent("Motion Threshold",
                "Motion vector magnitude at which sharpening is fully reduced");

            public static readonly GUIContent MotionReductionLabel = new GUIContent("Motion Reduction",
                "Amount of sharpening reduction in moving areas");

            // Mask
            public static readonly GUIContent MaskEnabledLabel = new GUIContent("Selective Masking",
                "Apply sharpening only where mask texture is white");

            public static readonly GUIContent MaskTextureLabel = new GUIContent("Mask Texture",
                "Grayscale mask: White = full sharpening, Black = no sharpening");
        }
    }
}
#endif
