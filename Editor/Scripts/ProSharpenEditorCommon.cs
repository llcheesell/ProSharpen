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
                "CAS: 高速でアーティファクトに強い (AMD FidelityFX CAS 準拠)\n" +
                "Unsharp Mask: 半径を指定できる写真的なシャープ\n" +
                "Adaptive: 質感を強め、強いエッジは方向性シャープで保護\n" +
                "RCAS: クリップ限界を解く FSR のシャープ (ノイズ抑制付き)");

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
                "局所コントラストがこの値未満の平坦部はシャープしない。ノイズの増幅を防ぐ (USM / RCAS / Adaptive)");

            public static readonly GUIContent ClampLimitLabel = new GUIContent("Detail Limit",
                "1 画素あたりの最大変化量 (知覚空間)。小さくすると高コントラストなエッジの強調が穏やかになる");

            public static readonly GUIContent RadiusLabel = new GUIContent("Radius",
                "ぼかし半径 (ガウスの標準偏差、ピクセル)。大きいほど太いディテールを強調する。\n" +
                "Realtime は約 1.3 まで、High は約 2.5 まで有効");

            // Halo control
            public static readonly GUIContent OvershootLabel = new GUIContent("Overshoot (Bright)",
                "明るい側のハロー許容量 (知覚空間)。近傍の最大値をどこまで超えてよいか。0 で白ハローなし");

            public static readonly GUIContent UndershootLabel = new GUIContent("Undershoot (Dark)",
                "暗い側のハロー許容量 (知覚空間)。近傍の最小値をどこまで下回ってよいか。0 で黒ハローなし");

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

        /// <summary>
        /// Returns true if EdgeProtection should be visible for the given mode.
        /// </summary>
        public static bool IsEdgeProtectionVisible(SharpenMode mode) => mode == SharpenMode.Adaptive;

        /// <summary>
        /// Returns true if DetailBoost should be visible for the given mode.
        /// </summary>
        public static bool IsDetailBoostVisible(SharpenMode mode) => mode == SharpenMode.Adaptive;

        /// <summary>
        /// Returns true if NoiseThreshold should be visible for the given mode.
        /// CAS 以外のモードで、平坦部のノイズゲートとして使用する。
        /// </summary>
        public static bool IsNoiseThresholdVisible(SharpenMode mode) =>
            mode != SharpenMode.CAS;

        /// <summary>
        /// Returns true if ClampLimit should be visible for the given mode.
        /// 共通リミッタの絶対上限として全モードで使用する。
        /// </summary>
        public static bool IsClampLimitVisible(SharpenMode mode) => true;

        /// <summary>
        /// Returns true if Radius should be visible for the given mode.
        /// </summary>
        public static bool IsRadiusVisible(SharpenMode mode) => mode == SharpenMode.UnsharpMask;

        /// <summary>
        /// Returns true if CornerWeight should be visible for the given mode and quality.
        /// </summary>
        public static bool IsCornerWeightVisible(SharpenMode mode, SharpenQuality quality) =>
            mode == SharpenMode.CAS && quality == SharpenQuality.High;
    }
}
#endif
