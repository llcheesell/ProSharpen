#if PROSHARPEN_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        [Tooltip("Noise threshold (USM mode).")]
        public ClampedFloatParameter noiseThreshold = new ClampedFloatParameter(0.02f, 0f, 0.1f);

        [Tooltip("Maximum sharpening adjustment.")]
        public ClampedFloatParameter clampLimit = new ClampedFloatParameter(0.15f, 0.01f, 0.5f);

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

        public override void Create()
        {
            _renderPass = new ProSharpenRenderPass
            {
                renderPassEvent = renderPassEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.postProcessEnabled)
            {
                var volume = VolumeManager.instance.stack.GetComponent<ProSharpenVolumeURP>();
                if (volume != null && volume.IsActive())
                {
                    renderer.EnqueuePass(_renderPass);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            _renderPass?.Dispose();
        }
    }

    /// <summary>
    /// URP Render Pass that executes the sharpening shader.
    /// </summary>
    sealed class ProSharpenRenderPass : ScriptableRenderPass, System.IDisposable
    {
        const string ProfilerTag = "ProSharpen";

        Material _material;
        RTHandle _tempRT;

        Material GetMaterial()
        {
            if (_material == null)
            {
                var shader = Shader.Find(ProSharpenShaderConstants.ShaderName);
                if (shader != null)
                    _material = CoreUtils.CreateEngineMaterial(shader);
            }
            return _material;
        }

#if PROSHARPEN_UNITY6
        // Unity 6+ uses Render Graph API
        public override void RecordRenderGraph(UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            ContextContainer frameData)
        {
            var material = GetMaterial();
            if (material == null) return;

            var volume = VolumeManager.instance.stack.GetComponent<ProSharpenVolumeURP>();
            if (volume == null || !volume.IsActive()) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraColorHandle = resourceData.activeColorTexture;

            var desc = renderGraph.GetTextureDesc(cameraColorHandle);
            desc.name = "_ProSharpenTemp";
            var tempHandle = renderGraph.CreateTexture(desc);

            ConfigureMaterial(material, volume);

            // Copy source to temp, apply sharpen from temp to destination
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(ProfilerTag, out var passData))
            {
                passData.source = cameraColorHandle;
                passData.material = material;
                builder.UseTexture(cameraColorHandle, UnityEngine.Rendering.RenderGraphModule.AccessFlags.Read);
                builder.SetRenderAttachment(tempHandle, 0);
                builder.SetRenderFunc((PassData data, UnityEngine.Rendering.RenderGraphModule.RasterGraphContext ctx) =>
                {
                    data.material.SetTexture(ProSharpenShaderConstants.MainTexId, data.source);
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(ProfilerTag + "_Copy", out var passData))
            {
                passData.source = tempHandle;
                passData.material = null;
                builder.UseTexture(tempHandle, UnityEngine.Rendering.RenderGraphModule.AccessFlags.Read);
                builder.SetRenderAttachment(cameraColorHandle, 0);
                builder.SetRenderFunc((PassData data, UnityEngine.Rendering.RenderGraphModule.RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }

        class PassData
        {
            public UnityEngine.Rendering.RenderGraphModule.TextureHandle source;
            public Material material;
        }
#else
        // Legacy path for Unity 2022.3 - 2023.x
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref _tempRT, desc, FilterMode.Bilinear,
                TextureWrapMode.Clamp, name: "_ProSharpenTemp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var material = GetMaterial();
            if (material == null) return;

            var volume = VolumeManager.instance.stack.GetComponent<ProSharpenVolumeURP>();
            if (volume == null || !volume.IsActive()) return;

            ConfigureMaterial(material, volume);

            var cmd = CommandBufferPool.Get(ProfilerTag);
            var cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

            // Blit source -> temp with sharpen, then temp -> source
            Blitter.BlitCameraTexture(cmd, cameraColorTarget, _tempRT, material, 0);
            Blitter.BlitCameraTexture(cmd, _tempRT, cameraColorTarget);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#endif

        void ConfigureMaterial(Material material, ProSharpenVolumeURP volume)
        {
            ProSharpenMaterialSetup.SetKeywords(material,
                volume.mode.value, volume.quality.value, volume.lumaOnly.value,
                volume.depthAware.value, volume.motionAware.value, volume.maskEnabled.value);

            ProSharpenMaterialSetup.SetProperties(material,
                volume.intensity.value, volume.sharpness.value, volume.edgeProtection.value,
                volume.detailBoost.value, volume.noiseThreshold.value, volume.clampLimit.value,
                volume.cornerWeight.value);

            if (volume.depthAware.value)
            {
                ProSharpenMaterialSetup.SetDepthProperties(material,
                    volume.depthNearStrength.value, volume.depthFarStrength.value,
                    volume.depthNearPlane.value, volume.depthFarPlane.value);
            }

            if (volume.motionAware.value)
            {
                ProSharpenMaterialSetup.SetMotionProperties(material,
                    volume.motionThreshold.value, volume.motionReduction.value);
            }

            if (volume.maskEnabled.value && volume.maskTexture.value != null)
            {
                material.SetTexture(ProSharpenShaderConstants.MaskTexId, volume.maskTexture.value);
            }
        }

        public void Dispose()
        {
            _tempRT?.Release();
            CoreUtils.Destroy(_material);
        }
    }
}
#endif
