#if PROSHARPEN_HDRP
using System.Collections.Generic;
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
    /// <summary>
    /// ProSharpenStencilMaskPass が描いたマスクを、カメラごとに ProSharpenVolume へ受け渡す。
    /// グローバルテクスチャはコマンド実行時まで反映されないため、記録時に読める C# 側の参照で渡す。
    /// </summary>
    public static class ProSharpenMaskRegistry
    {
        struct Entry
        {
            public RTHandle mask;
            public int frame;
        }

        static readonly Dictionary<Camera, Entry> s_Entries = new Dictionary<Camera, Entry>();
        static readonly List<Camera> s_Removal = new List<Camera>();

        internal static void Register(Camera camera, RTHandle mask)
        {
            s_Entries[camera] = new Entry { mask = mask, frame = Time.frameCount };
        }

        internal static void Unregister(Camera camera, RTHandle mask)
        {
            if (s_Entries.TryGetValue(camera, out var entry) && entry.mask == mask)
                s_Entries.Remove(camera);
        }

        /// <summary>
        /// このフレームで指定カメラ用に描かれたマスクを返す。無ければ null。
        /// </summary>
        public static Texture GetMask(Camera camera)
        {
            if (camera == null || !s_Entries.TryGetValue(camera, out var entry))
                return null;

            // マスクパスが無効化された後に古いマスクを使い続けない
            if (entry.frame != Time.frameCount || entry.mask == null)
                return null;

            return entry.mask.rt;
        }

        // 破棄済みカメラの登録を取り除く
        internal static void RemoveDestroyedCameras()
        {
            s_Removal.Clear();
            foreach (var pair in s_Entries)
            {
                if (pair.Key == null)
                    s_Removal.Add(pair.Key);
            }

            for (int i = 0; i < s_Removal.Count; i++)
                s_Entries.Remove(s_Removal[i]);
        }
    }

    public class ProSharpenStencilMaskPass : CustomPass
    {
        [Tooltip("Layers to include in the sharpening mask.")]
        public LayerMask sharpenLayers = -1;

        [Tooltip("Resolution scale for the mask texture.")]
        [Range(0.25f, 1f)]
        public float resolutionScale = 0.5f;

        [Tooltip("カメラ深度と比較し、手前の物体に隠れた部分をマスクから除外する。深度プリパスより前の Injection Point では無効にすること。")]
        public bool depthTest = true;

        // サイズの異なるカメラ (Game ビューと Scene ビューなど) が交互に描画しても
        // 再確保を繰り返さないよう、マスクはカメラごとに持つ
        readonly Dictionary<Camera, RTHandle> _maskRTs = new Dictionary<Camera, RTHandle>();
        readonly List<Camera> _removal = new List<Camera>();
        Material _maskWriteMaterial;
        ShaderTagId[] _shaderTags;


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

            var shader = Shader.Find(ProSharpenShaderConstants.MaskWriteShaderName);
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

            ReleaseDestroyedCameras();

            _maskRTs.TryGetValue(camera, out var _maskRT);
            if (_maskRT == null || _maskRT.rt == null || _maskRT.rt.width != w || _maskRT.rt.height != h)
            {
                if (_maskRT != null)
                {
                    ProSharpenMaskRegistry.Unregister(camera, _maskRT);
                    _maskRT.Release();
                }

                _maskRT = RTHandles.Alloc(w, h, colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                    name: "ProSharpen_StencilMask");
                _maskRTs[camera] = _maskRT;
            }

            // xy = マスクの 1 画素分の UV 幅、z = 深度テストの有効フラグ
            ctx.cmd.SetGlobalVector(ProSharpenShaderConstants.MaskParamsId,
                new Vector4(1f / w, 1f / h, depthTest ? 1f : 0f, 0f));

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

            // CoreUtils.DrawRendererList は Unity 6.3 で引数が変わったため、
            // 全バージョンで使える CommandBuffer の API を直接呼ぶ
            ctx.cmd.DrawRendererList(ctx.renderContext.CreateRendererList(result));

            // 他のシェーダーからも参照できるようグローバルにも設定する
            ctx.cmd.SetGlobalTexture(ProSharpenShaderConstants.GlobalMaskTexId, _maskRT);

            // ProSharpenVolume へはカメラ単位で受け渡す
            ProSharpenMaskRegistry.Register(camera, _maskRT);
        }

        // 破棄されたカメラのマスクを解放する
        void ReleaseDestroyedCameras()
        {
            _removal.Clear();
            foreach (var pair in _maskRTs)
            {
                if (pair.Key == null)
                    _removal.Add(pair.Key);
            }

            if (_removal.Count == 0)
                return;

            for (int i = 0; i < _removal.Count; i++)
            {
                _maskRTs[_removal[i]]?.Release();
                _maskRTs.Remove(_removal[i]);
            }

            ProSharpenMaskRegistry.RemoveDestroyedCameras();
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_maskWriteMaterial);
            _maskWriteMaterial = null;

            foreach (var pair in _maskRTs)
            {
                if (pair.Key != null)
                    ProSharpenMaskRegistry.Unregister(pair.Key, pair.Value);
                pair.Value?.Release();
            }

            _maskRTs.Clear();
        }
    }
}
#endif
