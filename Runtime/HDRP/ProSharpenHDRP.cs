#if PROSHARPEN_HDRP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LLCheesell.ProSharpen
{
    /// <summary>
    /// HDRP Volume Component for ProSharpen post-processing.
    /// Add this to a Volume profile to enable sharpening.
    /// </summary>
    [System.Serializable, VolumeComponentMenu("Post-processing/ProSharpen")]
    public sealed class ProSharpenVolume : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        // --- Core Settings ---
        [Header("Core")]
        [Tooltip("Enable/disable the sharpening effect.")]
        public BoolParameter enabled = new BoolParameter(false);

        [Tooltip("Sharpening algorithm to use.")]
        public SharpenModeParameter mode = new SharpenModeParameter(SharpenMode.CAS);

        [Tooltip("Quality level. Realtime is optimized for performance, High uses extended sampling.")]
        public SharpenQualityParameter quality = new SharpenQualityParameter(SharpenQuality.Realtime);

        [Tooltip("Overall effect intensity. Blends between original and sharpened.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Sharpening strength. Higher values produce stronger sharpening.")]
        public ClampedFloatParameter sharpness = new ClampedFloatParameter(0.5f, 0f, 1f);

        // --- Advanced ---
        [Header("Advanced")]
        [Tooltip("Process in luminance space only to prevent color shifts.")]
        public BoolParameter lumaOnly = new BoolParameter(true);

        [Tooltip("Edge protection strength. Reduces sharpening on strong edges to prevent jaggies.")]
        public ClampedFloatParameter edgeProtection = new ClampedFloatParameter(0.5f, 0f, 1f);

        [Tooltip("Detail boost for low-contrast areas.")]
        public ClampedFloatParameter detailBoost = new ClampedFloatParameter(0.3f, 0f, 1f);

        [Tooltip("ノイズしきい値。局所コントラストがこの値未満の平坦部はシャープしない (USM / RCAS / Adaptive)。")]
        public ClampedFloatParameter noiseThreshold = new ClampedFloatParameter(0.02f, 0f, 0.1f);

        [Tooltip("1 画素あたりの最大変化量 (知覚空間)。高コントラストなエッジの過剰な強調を抑える。")]
        public ClampedFloatParameter clampLimit = new ClampedFloatParameter(0.15f, 0.01f, 0.5f);

        [Tooltip("ぼかし半径 (ガウスの標準偏差、ピクセル)。大きいほど太いディテールを強調する (USM)。Realtime は約 1.3 まで、High は約 2.5 まで有効。")]
        public ClampedFloatParameter radius = new ClampedFloatParameter(1f, 0.5f, 3f);

        // --- Halo Control ---
        [Header("Halo Control")]
        [Tooltip("明るい側のハロー許容量 (知覚空間)。近傍の最大値をどこまで超えてよいか。0 で白ハローなし。")]
        public ClampedFloatParameter overshoot = new ClampedFloatParameter(0.01f, 0f, 0.25f);

        [Tooltip("暗い側のハロー許容量 (知覚空間)。近傍の最小値をどこまで下回ってよいか。0 で黒ハローなし。")]
        public ClampedFloatParameter undershoot = new ClampedFloatParameter(0.03f, 0f, 0.25f);

        [Tooltip("Corner sample weight for CAS High Quality mode.")]
        public ClampedFloatParameter cornerWeight = new ClampedFloatParameter(0.5f, 0f, 1f);

        // --- Depth-Aware ---
        [Header("Depth-Aware")]
        [Tooltip("Enable depth-aware sharpening modulation.")]
        public BoolParameter depthAware = new BoolParameter(false);

        [Tooltip("Sharpening strength for near objects.")]
        public ClampedFloatParameter depthNearStrength = new ClampedFloatParameter(1f, 0f, 2f);

        [Tooltip("Sharpening strength for far objects.")]
        public ClampedFloatParameter depthFarStrength = new ClampedFloatParameter(0.5f, 0f, 2f);

        [Tooltip("Near plane distance for depth falloff.")]
        public MinFloatParameter depthNearPlane = new MinFloatParameter(0.1f, 0f);

        [Tooltip("Far plane distance for depth falloff.")]
        public MinFloatParameter depthFarPlane = new MinFloatParameter(100f, 0.1f);

        // --- Motion-Aware (TAA/DLSS Recovery) ---
        [Header("TAA/DLSS Recovery")]
        [Tooltip("Enable motion-aware sharpening. Sharpens static areas more to recover TAA/DLSS softness.")]
        public BoolParameter motionAware = new BoolParameter(false);

        [Tooltip("Motion vector magnitude threshold for full sharpening reduction.")]
        public ClampedFloatParameter motionThreshold = new ClampedFloatParameter(0.01f, 0.001f, 0.1f);

        [Tooltip("How much to reduce sharpening in moving areas.")]
        public ClampedFloatParameter motionReduction = new ClampedFloatParameter(0.8f, 0f, 1f);

        // --- Mask ---
        [Header("Selective Masking")]
        [Tooltip("Enable mask-based selective sharpening.")]
        public BoolParameter maskEnabled = new BoolParameter(false);

        [Tooltip("マスクテクスチャ。白 = シャープあり、黒 = シャープなし。未指定の場合は ProSharpenStencilMaskPass が生成したマスクを使う。")]
        public TextureParameter maskTexture = new TextureParameter(null);

        Material _material;

        public bool IsActive() => _material != null && enabled.value && intensity.value > 0f;

        public override CustomPostProcessInjectionPoint injectionPoint =>
            CustomPostProcessInjectionPoint.AfterPostProcess;

        public override void Setup()
        {
            var shader = Shader.Find(ProSharpenShaderConstants.ShaderName);
            if (shader != null)
                _material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
        {
            if (_material == null)
                return;

            // マスク: テクスチャ指定を優先し、無ければレイヤーマスク用パスのグローバルマスクを使う
            Texture mask = null;
            if (maskEnabled.value)
            {
                mask = maskTexture.value;
                if (mask == null)
                    mask = ProSharpenMaskRegistry.GetMask(camera.camera);
            }

            ProSharpenMaterialSetup.SetKeywords(_material,
                mode.value, quality.value, lumaOnly.value,
                depthAware.value, motionAware.value, mask != null);

            ProSharpenMaterialSetup.SetProperties(_material,
                intensity.value, sharpness.value, edgeProtection.value,
                detailBoost.value, noiseThreshold.value, clampLimit.value,
                cornerWeight.value, overshoot.value, undershoot.value);

            if (mode.value == SharpenMode.UnsharpMask)
                ProSharpenMaterialSetup.SetBlurKernel(_material, radius.value, quality.value);

            if (depthAware.value)
            {
                ProSharpenMaterialSetup.SetDepthProperties(_material,
                    depthNearStrength.value, depthFarStrength.value,
                    depthNearPlane.value, depthFarPlane.value);
            }

            if (motionAware.value)
            {
                ProSharpenMaterialSetup.SetMotionProperties(_material,
                    motionThreshold.value, motionReduction.value);
            }

            if (mask != null)
                _material.SetTexture(ProSharpenShaderConstants.MaskTexId, mask);

            _material.SetTexture(ProSharpenShaderConstants.MainTexId, source);
            HDUtils.DrawFullScreen(cmd, _material, destination, shaderPassId: 0);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(_material);
        }
    }

    // Custom VolumeParameter types for enums
    [System.Serializable]
    public sealed class SharpenModeParameter : VolumeParameter<SharpenMode>
    {
        public SharpenModeParameter(SharpenMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [System.Serializable]
    public sealed class SharpenQualityParameter : VolumeParameter<SharpenQuality>
    {
        public SharpenQualityParameter(SharpenQuality value, bool overrideState = false)
            : base(value, overrideState) { }
    }
}
#endif
