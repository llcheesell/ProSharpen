#if PROSHARPEN_HDRP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.HighDefinition;

namespace LLCheesell.ProSharpen
{
    /// <summary>
    /// HDRP Custom Pass that writes a stencil/color mask for selective sharpening.
    /// More efficient than a separate camera approach.
    ///
    /// Usage:
    /// 1. Add a Custom Pass Volume to your scene
    /// 2. Add this pass to render objects on the sharpen layer
    /// 3. The resulting mask texture is automatically used by ProSharpenVolume
    /// </summary>
    public class ProSharpenStencilMaskPass : CustomPass
    {
        [Tooltip("Layers to include in the sharpening mask.")]
        public LayerMask sharpenLayers = -1;

        [Tooltip("Resolution scale for the mask texture.")]
        [Range(0.25f, 1f)]
        public float resolutionScale = 0.5f;

        RTHandle _maskRT;
        Material _maskWriteMaterial;
        ShaderTagId[] _shaderTags;

        static readonly int _globalMaskTexId = Shader.PropertyToID("_ProSharpenGlobalMask");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            _shaderTags = new ShaderTagId[]
            {
                new ShaderTagId("Forward"),
                new ShaderTagId("ForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("GBuffer"),
                new ShaderTagId("DepthForwardOnly")
            };

            var shader = Shader.Find("Hidden/ProSharpen/MaskWrite");
            if (shader != null)
                _maskWriteMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (_maskWriteMaterial == null) return;

            // Allocate mask RT
            var camera = ctx.hdCamera.camera;
            int w = Mathf.Max(1, (int)(camera.pixelWidth * resolutionScale));
            int h = Mathf.Max(1, (int)(camera.pixelHeight * resolutionScale));

            if (_maskRT == null || _maskRT.rt.width != w || _maskRT.rt.height != h)
            {
                _maskRT?.Release();
                _maskRT = RTHandles.Alloc(w, h, colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                    name: "ProSharpen_StencilMask");
            }

            // Clear to black
            CoreUtils.SetRenderTarget(ctx.cmd, _maskRT, ClearFlag.Color, Color.black);

            // Render matching objects as white
            var result = new RendererListDesc(_shaderTags, ctx.cullingResults, ctx.hdCamera.camera)
            {
                rendererConfiguration = PerObjectData.None,
                renderQueueRange = RenderQueueRange.all,
                sortingCriteria = SortingCriteria.CommonOpaque,
                layerMask = sharpenLayers,
                overrideMaterial = _maskWriteMaterial,
                overrideMaterialPassIndex = 0
            };

            CoreUtils.DrawRendererList(ctx.cmd,
                ctx.renderContext.CreateRendererList(result));

            // Set as global texture for the sharpen pass
            ctx.cmd.SetGlobalTexture(_globalMaskTexId, _maskRT);
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_maskWriteMaterial);
            _maskRT?.Release();
        }
    }
}
#endif
