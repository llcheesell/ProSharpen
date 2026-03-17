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
        Adaptive = 2
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

        // Shader keywords
        public const string KeywordCAS = "PROSHARPEN_CAS";
        public const string KeywordUnsharp = "PROSHARPEN_UNSHARP";
        public const string KeywordAdaptive = "PROSHARPEN_ADAPTIVE";
        public const string KeywordHighQuality = "PROSHARPEN_HIGH_QUALITY";
        public const string KeywordLumaOnly = "PROSHARPEN_LUMA_ONLY";
        public const string KeywordDepthAware = "PROSHARPEN_DEPTH_AWARE";
        public const string KeywordMotionAware = "PROSHARPEN_MOTION_AWARE";
        public const string KeywordMaskEnabled = "PROSHARPEN_MASK_ENABLED";

        // Shader path
        public const string ShaderName = "Hidden/ProSharpen/ProSharpen";
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

            // Quality
            SetKeyword(material, ProSharpenShaderConstants.KeywordHighQuality, quality == SharpenQuality.High);

            // Options
            SetKeyword(material, ProSharpenShaderConstants.KeywordLumaOnly, lumaOnly);
            SetKeyword(material, ProSharpenShaderConstants.KeywordDepthAware, depthAware);
            SetKeyword(material, ProSharpenShaderConstants.KeywordMotionAware, motionAware);
            SetKeyword(material, ProSharpenShaderConstants.KeywordMaskEnabled, maskEnabled);
        }

        public static void SetProperties(Material material, float intensity, float sharpness,
            float edgeProtection, float detailBoost, float noiseThreshold, float clampLimit,
            float cornerWeight)
        {
            material.SetFloat(ProSharpenShaderConstants.IntensityId, intensity);
            material.SetFloat(ProSharpenShaderConstants.SharpnessId, sharpness);
            material.SetFloat(ProSharpenShaderConstants.EdgeProtectionId, edgeProtection);
            material.SetFloat(ProSharpenShaderConstants.DetailBoostId, detailBoost);
            material.SetFloat(ProSharpenShaderConstants.NoiseThresholdId, noiseThreshold);
            material.SetFloat(ProSharpenShaderConstants.ClampLimitId, clampLimit);
            material.SetFloat(ProSharpenShaderConstants.CornerWeightId, cornerWeight);
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
