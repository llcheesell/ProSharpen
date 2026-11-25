// URP 17.3 (Unity 6.3) 以降、Execute などの Compatibility Mode 用 API は
// URP_COMPATIBILITY_MODE 定義時のみ存在する。それ以前は常に存在する
#if !PROSHARPEN_URP_17_3 || URP_COMPATIBILITY_MODE
#define PROSHARPEN_URP_COMPAT
#endif

#if PROSHARPEN_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if PROSHARPEN_UNITY6
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace LLCheesell.ProSharpen
{
    /// <summary>
    /// URP Volume Component for ProSharpen.
    /// Controls sharpening parameters via the Volume system.
    /// </summary>
    [System.Serializable, VolumeComponentMenu("Post-processing/ProSharpen")]
    public sealed class ProSharpenVolumeURP : VolumeComponent, IPostProcessComponent
    {
        // --- Core Settings ---
        [Header("Core")]
        [Tooltip("Sharpening algorithm to use.")]
        public SharpenModeParameterURP mode = new SharpenModeParameterURP(SharpenMode.CAS);

        [Tooltip("Quality level.")]
        public SharpenQualityParameterURP quality = new SharpenQualityParameterURP(SharpenQuality.Realtime);

        [Tooltip("Overall effect intensity.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Sharpening strength.")]
        public ClampedFloatParameter sharpness = new ClampedFloatParameter(0.5f, 0f, 1f);

        // --- Advanced ---
        [Header("Advanced")]
        [Tooltip("Process in luminance space only to prevent color shifts.")]
        public BoolParameter lumaOnly = new BoolParameter(true);

        [Tooltip("Edge protection strength.")]
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

        [Tooltip("Corner sample weight for CAS HQ.")]
        public ClampedFloatParameter cornerWeight = new ClampedFloatParameter(0.5f, 0f, 1f);

        // --- Depth-Aware ---
        [Header("Depth-Aware")]
        [Tooltip("Enable depth-aware sharpening.")]
        public BoolParameter depthAware = new BoolParameter(false);

        [Tooltip("Near object sharpening strength.")]
        public ClampedFloatParameter depthNearStrength = new ClampedFloatParameter(1f, 0f, 2f);

        [Tooltip("Far object sharpening strength.")]
        public ClampedFloatParameter depthFarStrength = new ClampedFloatParameter(0.5f, 0f, 2f);

        [Tooltip("Near plane distance.")]
        public MinFloatParameter depthNearPlane = new MinFloatParameter(0.1f, 0f);

        [Tooltip("Far plane distance.")]
        public MinFloatParameter depthFarPlane = new MinFloatParameter(100f, 0.1f);

        // --- Motion-Aware ---
        [Header("TAA/DLSS Recovery")]
        [Tooltip("Enable motion-aware sharpening for TAA recovery.")]
        public BoolParameter motionAware = new BoolParameter(false);

        [Tooltip("Motion threshold.")]
        public ClampedFloatParameter motionThreshold = new ClampedFloatParameter(0.01f, 0.001f, 0.1f);

        [Tooltip("Motion reduction amount.")]
        public ClampedFloatParameter motionReduction = new ClampedFloatParameter(0.8f, 0f, 1f);

        // --- Mask ---
        [Header("Selective Masking")]
        [Tooltip("Enable mask-based selective sharpening.")]
        public BoolParameter maskEnabled = new BoolParameter(false);

        [Tooltip("Mask texture (white = sharpen, black = skip).")]
        public TextureParameter maskTexture = new TextureParameter(null);

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => false;
    }

    [System.Serializable]
    public sealed class SharpenModeParameterURP : VolumeParameter<SharpenMode>
    {
        public SharpenModeParameterURP(SharpenMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [System.Serializable]
    public sealed class SharpenQualityParameterURP : VolumeParameter<SharpenQuality>
    {
        public SharpenQualityParameterURP(SharpenQuality value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    /// <summary>
    /// URP Renderer Feature that adds the ProSharpen pass.
    /// Add this to your URP Renderer Data asset.
    /// </summary>
    public sealed class ProSharpenRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("When to inject the sharpening pass.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        ProSharpenRenderPass _renderPass;
        Material _material;

        public override void Create()
        {
            var shader = Shader.Find(ProSharpenShaderConstants.ShaderName);
            if (shader != null)
                _material = CoreUtils.CreateEngineMaterial(shader);

            _renderPass = new ProSharpenRenderPass(_material)
            {
                renderPassEvent = renderPassEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null)
                return;

            if (renderingData.cameraData.cameraType != CameraType.Game)
                return;

            if (!renderingData.cameraData.postProcessEnabled)
                return;

            var volume = VolumeManager.instance.stack.GetComponent<ProSharpenVolumeURP>();
            if (volume == null || !volume.IsActive())
                return;

            // 深度連動を使う場合のみ、深度テクスチャの生成を要求する
            _renderPass.ConfigureInput(volume.depthAware.value
                ? ScriptableRenderPassInput.Depth
                : ScriptableRenderPassInput.None);

            renderer.EnqueuePass(_renderPass);
        }

#if PROSHARPEN_URP_COMPAT
        // URP 14 以降の要件: レンダーターゲットは AddRenderPasses ではなくここで取得する
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Game)
                _renderPass.SetTarget(renderer.cameraColorTargetHandle);
        }
#endif

        protected override void Dispose(bool disposing)
        {
            _renderPass?.Dispose();
            CoreUtils.Destroy(_material);
        }
    }

    /// <summary>
    /// URP Render Pass that executes the sharpening shader.
    /// Implements both Execute (Compatibility Mode) and RecordRenderGraph (Unity 6+).
    /// </summary>
    sealed class ProSharpenRenderPass : ScriptableRenderPass, System.IDisposable
    {
        const string ProfilerTag = "ProSharpen";

        Material _material;

        public ProSharpenRenderPass(Material material)
        {
            _material = material;

#if PROSHARPEN_UNITY6
            // バックバッファは読み取れないため、中間テクスチャへの描画を要求する
            requiresIntermediateTexture = true;
#endif
        }

        void ConfigureMaterial(ProSharpenVolumeURP volume)
        {
            Texture mask = volume.maskEnabled.value ? volume.maskTexture.value : null;

            ProSharpenMaterialSetup.SetKeywords(_material,
                volume.mode.value, volume.quality.value, volume.lumaOnly.value,
                volume.depthAware.value, false, mask != null);

            ProSharpenMaterialSetup.SetProperties(_material,
                volume.intensity.value, volume.sharpness.value, volume.edgeProtection.value,
                volume.detailBoost.value, volume.noiseThreshold.value, volume.clampLimit.value,
                volume.cornerWeight.value, volume.overshoot.value, volume.undershoot.value);

            if (volume.mode.value == SharpenMode.UnsharpMask)
                ProSharpenMaterialSetup.SetBlurKernel(_material, volume.radius.value, volume.quality.value);

            if (volume.depthAware.value)
            {
                ProSharpenMaterialSetup.SetDepthProperties(_material,
                    volume.depthNearStrength.value, volume.depthFarStrength.value,
                    volume.depthNearPlane.value, volume.depthFarPlane.value);
            }

            if (mask != null)
                _material.SetTexture(ProSharpenShaderConstants.MaskTexId, mask);
        }

#if PROSHARPEN_UNITY6
        // ====================================================================
        // Unity 6+ Render Graph path
        // ====================================================================
        class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null) return;

            var volume = VolumeManager.instance.stack.GetComponent<ProSharpenVolumeURP>();
            if (volume == null || !volume.IsActive()) return;

            var resourceData = frameData.Get<UniversalResourceData>();

            // バックバッファはサンプルできないため、その場合は何もしない
            if (resourceData.isActiveTargetBackBuffer) return;

            var sourceHandle = resourceData.activeColorTexture;
            if (!sourceHandle.IsValid()) return;

            ConfigureMaterial(volume);

            // カメラカラーと同じ形式で、深度と MSAA を持たない一時テクスチャを作る
            var desc = renderGraph.GetTextureDesc(sourceHandle);
            bool sourceIsMultisampled = (int)desc.msaaSamples > 1;
            desc.name = "_ProSharpenTemp";
            desc.clearBuffer = false;
            desc.depthBufferBits = DepthBits.None;
            desc.msaaSamples = MSAASamples.None;
            var tempHandle = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(ProfilerTag, out var passData))
            {
                passData.source = sourceHandle;
                passData.material = _material;
                builder.UseTexture(sourceHandle);

                if (volume.depthAware.value && resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture);

                builder.SetRenderAttachment(tempHandle, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source,
                        new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            if (!sourceIsMultisampled)
            {
                // 一時テクスチャを新しいカメラカラーにする (コピーし戻す必要がない)
                resourceData.cameraColor = tempHandle;
                return;
            }

            // MSAA のカメラカラー (ポストプロセスより前のイベント) を差し替えると、後続パスで
            // MSAA の深度とサンプル数が合わなくなるため、元のカラーへコピーし戻す
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(ProfilerTag + " Copy", out var passData))
            {
                passData.source = tempHandle;
                builder.UseTexture(tempHandle);
                builder.SetRenderAttachment(sourceHandle, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), 0f, false);
                });
            }
        }
#endif

#if PROSHARPEN_URP_COMPAT
        // ====================================================================
        // Compatibility Mode path (Unity 2022.3 - 2023.x, or Unity 6 compat)
        // ====================================================================
        static readonly ProfilingSampler _profilingSampler = new ProfilingSampler(ProfilerTag);

        RTHandle _tempRT;
        RTHandle _cameraColorTarget;

        public void SetTarget(RTHandle colorTarget)
        {
            _cameraColorTarget = colorTarget;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
#if PROSHARPEN_UNITY6
            RenderingUtils.ReAllocateHandleIfNeeded(ref _tempRT, desc, FilterMode.Bilinear,
                TextureWrapMode.Clamp, name: "_ProSharpenTemp");
#else
            RenderingUtils.ReAllocateIfNeeded(ref _tempRT, desc, FilterMode.Bilinear,
                TextureWrapMode.Clamp, name: "_ProSharpenTemp");
#endif
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null || _cameraColorTarget == null) return;

            var volume = VolumeManager.instance.stack.GetComponent<ProSharpenVolumeURP>();
            if (volume == null || !volume.IsActive()) return;

            ConfigureMaterial(volume);

            var cmd = CommandBufferPool.Get(ProfilerTag);
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                // Blit source -> temp with sharpen, then temp -> source
                Blitter.BlitCameraTexture(cmd, _cameraColorTarget, _tempRT, _material, 0);
                Blitter.BlitCameraTexture(cmd, _tempRT, _cameraColorTarget);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _tempRT?.Release();
            _tempRT = null;
        }
#else
        public void Dispose()
        {
        }
#endif
    }
}
#endif
