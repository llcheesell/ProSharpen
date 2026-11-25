using System;
using UnityEngine;

namespace LLCheesell.ProSharpen
{
    /// <summary>
    /// Sharpening algorithm selection.
    /// </summary>
    public enum SharpenMode
    {
        /// <summary>Contrast Adaptive Sharpening - fast, artifact-resistant. Best for real-time.</summary>
        CAS = 0,
        /// <summary>Unsharp Mask - classic photographic sharpening with fine control.</summary>
        UnsharpMask = 1,
        /// <summary>Adaptive Sharpening - context-aware with edge protection. Highest quality.</summary>
        Adaptive = 2,
        /// <summary>Robust CAS (FSR RCAS) - mathematically optimal sharpening with noise awareness.</summary>
        RCAS = 3
    }

    /// <summary>
    /// Quality level affecting kernel size and processing.
    /// </summary>
    public enum SharpenQuality
    {
        /// <summary>Optimized for real-time rendering. Uses 3x3 kernel.</summary>
        Realtime = 0,
        /// <summary>Higher quality with extended neighborhood sampling.</summary>
        High = 1
    }

    /// <summary>
    /// Shared shader keyword and property constants.
    /// </summary>
    public static class ProSharpenShaderConstants
    {
        // Shader property IDs (cached for performance)
        public static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        public static readonly int SharpnessId = Shader.PropertyToID("_Sharpness");
        public static readonly int EdgeProtectionId = Shader.PropertyToID("_EdgeProtection");
        public static readonly int DetailBoostId = Shader.PropertyToID("_DetailBoost");
        public static readonly int NoiseThresholdId = Shader.PropertyToID("_NoiseThreshold");
        public static readonly int ClampLimitId = Shader.PropertyToID("_ClampLimit");
        public static readonly int CornerWeightId = Shader.PropertyToID("_CornerWeight");
        public static readonly int OvershootId = Shader.PropertyToID("_Overshoot");
        public static readonly int UndershootId = Shader.PropertyToID("_Undershoot");
        public static readonly int BlurParamsId = Shader.PropertyToID("_BlurParams");

        public static readonly int DepthNearStrengthId = Shader.PropertyToID("_DepthNearStrength");
        public static readonly int DepthFarStrengthId = Shader.PropertyToID("_DepthFarStrength");
        public static readonly int DepthNearPlaneId = Shader.PropertyToID("_DepthNearPlane");
        public static readonly int DepthFarPlaneId = Shader.PropertyToID("_DepthFarPlane");

        public static readonly int MotionThresholdId = Shader.PropertyToID("_MotionThreshold");
        public static readonly int MotionReductionId = Shader.PropertyToID("_MotionReduction");

        public static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        public static readonly int DepthTexId = Shader.PropertyToID("_DepthTex");
        public static readonly int MotionVectorTexId = Shader.PropertyToID("_MotionVectorTex");
        public static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");

        // レイヤーマスク用パスが書き出すグローバルマスク
        public static readonly int GlobalMaskTexId = Shader.PropertyToID("_ProSharpenGlobalMask");
        public static readonly int MaskParamsId = Shader.PropertyToID("_ProSharpenMaskParams");

        // Shader keywords
        public const string KeywordCAS = "PROSHARPEN_CAS";
        public const string KeywordUnsharp = "PROSHARPEN_UNSHARP";
        public const string KeywordAdaptive = "PROSHARPEN_ADAPTIVE";
        public const string KeywordRCAS = "PROSHARPEN_RCAS";
        public const string KeywordHighQuality = "PROSHARPEN_HIGH_QUALITY";
        public const string KeywordLumaOnly = "PROSHARPEN_LUMA_ONLY";
        public const string KeywordDepthAware = "PROSHARPEN_DEPTH_AWARE";
        public const string KeywordMotionAware = "PROSHARPEN_MOTION_AWARE";
        public const string KeywordMaskEnabled = "PROSHARPEN_MASK_ENABLED";

        // Shader path
        public const string ShaderName = "Hidden/ProSharpen/ProSharpen";
        public const string MaskWriteShaderName = "Hidden/ProSharpen/MaskWrite";
    }

    /// <summary>
    /// Helper to configure material keywords from settings.
    /// </summary>
    public static class ProSharpenMaterialSetup
    {
        public static void SetKeywords(Material material, SharpenMode mode, SharpenQuality quality,
            bool lumaOnly, bool depthAware, bool motionAware, bool maskEnabled)
        {
            // Mode keywords (mutually exclusive)
            SetKeyword(material, ProSharpenShaderConstants.KeywordCAS, mode == SharpenMode.CAS);
            SetKeyword(material, ProSharpenShaderConstants.KeywordUnsharp, mode == SharpenMode.UnsharpMask);
            SetKeyword(material, ProSharpenShaderConstants.KeywordAdaptive, mode == SharpenMode.Adaptive);
            SetKeyword(material, ProSharpenShaderConstants.KeywordRCAS, mode == SharpenMode.RCAS);

            // Quality
            SetKeyword(material, ProSharpenShaderConstants.KeywordHighQuality, quality == SharpenQuality.High);

            // Options
            SetKeyword(material, ProSharpenShaderConstants.KeywordLumaOnly, lumaOnly);
            SetKeyword(material, ProSharpenShaderConstants.KeywordDepthAware, depthAware);
            SetKeyword(material, ProSharpenShaderConstants.KeywordMotionAware, motionAware);
            SetKeyword(material, ProSharpenShaderConstants.KeywordMaskEnabled, maskEnabled);
        }

        /// <summary>
        /// 旧シグネチャとの互換用。Overshoot / Undershoot は既定値を使う。
        /// </summary>
        public static void SetProperties(Material material, float intensity, float sharpness,
            float edgeProtection, float detailBoost, float noiseThreshold, float clampLimit,
            float cornerWeight)
        {
            SetProperties(material, intensity, sharpness, edgeProtection, detailBoost,
                noiseThreshold, clampLimit, cornerWeight, 0.01f, 0.03f);
        }

        public static void SetProperties(Material material, float intensity, float sharpness,
            float edgeProtection, float detailBoost, float noiseThreshold, float clampLimit,
            float cornerWeight, float overshoot, float undershoot)
        {
            material.SetFloat(ProSharpenShaderConstants.IntensityId, intensity);
            material.SetFloat(ProSharpenShaderConstants.SharpnessId, sharpness);
            material.SetFloat(ProSharpenShaderConstants.EdgeProtectionId, edgeProtection);
            material.SetFloat(ProSharpenShaderConstants.DetailBoostId, detailBoost);
            material.SetFloat(ProSharpenShaderConstants.NoiseThresholdId, noiseThreshold);
            material.SetFloat(ProSharpenShaderConstants.ClampLimitId, clampLimit);
            material.SetFloat(ProSharpenShaderConstants.CornerWeightId, cornerWeight);
            material.SetFloat(ProSharpenShaderConstants.OvershootId, overshoot);
            material.SetFloat(ProSharpenShaderConstants.UndershootId, undershoot);
        }

        /// <summary>
        /// Unsharp Mask 用のガウス核をマテリアルへ設定する。
        /// 隣り合う 2 テクセルを 1 回のバイリニアサンプルで読むためのオフセットと重みを求める。
        /// Realtime は 5x5 (3x3 タップ)、High は 9x9 (5x5 タップ) のガウスに相当する。
        /// </summary>
        /// <param name="material">設定先のマテリアル。</param>
        /// <param name="radius">ガウスの標準偏差 (ピクセル)。</param>
        /// <param name="quality">品質レベル。</param>
        public static void SetBlurKernel(Material material, float radius, SharpenQuality quality)
        {
            material.SetVector(ProSharpenShaderConstants.BlurParamsId, ComputeBlurParams(radius, quality));
        }

        /// <summary>
        /// ガウス核のパラメータを計算する。
        /// x = 内側タップのオフセット、y = 外側タップのオフセット、z = 内側タップの重み、w = 外側タップの重み。
        /// 重みは 1 軸分で、中心の重みは 1 - 2 * (z + w)。
        /// </summary>
        public static Vector4 ComputeBlurParams(float radius, SharpenQuality quality)
        {
            float sigma = Mathf.Max(radius, 0.1f);
            bool high = quality == SharpenQuality.High;

            float w0 = 1f;
            float w1 = Gaussian(1f, sigma);
            float w2 = Gaussian(2f, sigma);
            float w3 = high ? Gaussian(3f, sigma) : 0f;
            float w4 = high ? Gaussian(4f, sigma) : 0f;
            float sum = w0 + 2f * (w1 + w2 + w3 + w4);

            float inner = w1 + w2;
            float outer = w3 + w4;

            // 外側の重みが実質ゼロのときはオフセットを既定位置にしてゼロ除算を避ける
            float innerOffset = inner > 1e-8f ? (w1 + 2f * w2) / inner : 1f;
            float outerOffset = outer > 1e-8f ? (3f * w3 + 4f * w4) / outer : 3f;

            return new Vector4(innerOffset, outerOffset, inner / sum, outer / sum);
        }

        static float Gaussian(float x, float sigma)
        {
            return Mathf.Exp(-0.5f * x * x / (sigma * sigma));
        }

        public static void SetDepthProperties(Material material, float nearStrength, float farStrength,
            float nearPlane, float farPlane)
        {
            material.SetFloat(ProSharpenShaderConstants.DepthNearStrengthId, nearStrength);
            material.SetFloat(ProSharpenShaderConstants.DepthFarStrengthId, farStrength);
            material.SetFloat(ProSharpenShaderConstants.DepthNearPlaneId, nearPlane);
            material.SetFloat(ProSharpenShaderConstants.DepthFarPlaneId, farPlane);
        }

        public static void SetMotionProperties(Material material, float threshold, float reduction)
        {
            material.SetFloat(ProSharpenShaderConstants.MotionThresholdId, threshold);
            material.SetFloat(ProSharpenShaderConstants.MotionReductionId, reduction);
        }

        static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }
    }
}
