using UnityEngine;
using UnityEngine.Rendering;

namespace LLCheesell.ProSharpen
{
    /// <summary>
    /// Generates a mask render texture for selective sharpening.
    /// Renders specified layers as white on black to create a sharpening mask.
    /// Attach this to the camera or a manager object.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class ProSharpenMaskGenerator : MonoBehaviour
    {
        [Tooltip("Layers to apply sharpening to. Objects on these layers will be sharpened.")]
        public LayerMask sharpenLayers = -1;

        [Tooltip("Mask resolution scale relative to screen. Lower = faster, higher = more precise.")]
        [Range(0.25f, 1f)]
        public float resolutionScale = 0.5f;

        [Tooltip("Feather the mask edges for smoother transitions.")]
        [Range(0, 4)]
        public int blurIterations = 1;

        /// <summary>
        /// The generated mask texture. Assign this to the ProSharpen mask texture parameter.
        /// </summary>
        public RenderTexture MaskTexture => _maskRT;

        RenderTexture _maskRT;
        Camera _maskCamera;
        Camera _mainCamera;

        static readonly int _maskShaderTag = Shader.PropertyToID("_ProSharpenMask");

        void OnEnable()
        {
            _mainCamera = GetComponent<Camera>();
            SetupMaskCamera();
        }

        void OnDisable()
        {
            CleanupMaskCamera();
        }

        void LateUpdate()
        {
            if (_mainCamera == null) return;

            EnsureMaskRT();
            RenderMask();
        }

        void SetupMaskCamera()
        {
            var maskCamGO = new GameObject("ProSharpen_MaskCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            maskCamGO.transform.SetParent(transform, false);

            _maskCamera = maskCamGO.AddComponent<Camera>();
            _maskCamera.enabled = false; // We render manually
            _maskCamera.backgroundColor = Color.black;
            _maskCamera.clearFlags = CameraClearFlags.SolidColor;
        }

        void CleanupMaskCamera()
        {
            if (_maskCamera != null)
            {
                if (Application.isPlaying)
                    Destroy(_maskCamera.gameObject);
                else
                    DestroyImmediate(_maskCamera.gameObject);
            }

            if (_maskRT != null)
            {
                _maskRT.Release();
                if (Application.isPlaying)
                    Destroy(_maskRT);
                else
                    DestroyImmediate(_maskRT);
                _maskRT = null;
            }
        }

        void EnsureMaskRT()
        {
            int w = Mathf.Max(1, (int)(_mainCamera.pixelWidth * resolutionScale));
            int h = Mathf.Max(1, (int)(_mainCamera.pixelHeight * resolutionScale));

            if (_maskRT != null && _maskRT.width == w && _maskRT.height == h)
                return;

            if (_maskRT != null)
            {
                _maskRT.Release();
                if (Application.isPlaying)
                    Destroy(_maskRT);
                else
                    DestroyImmediate(_maskRT);
            }

            _maskRT = new RenderTexture(w, h, 16, RenderTextureFormat.R8)
            {
                name = "ProSharpen_Mask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _maskRT.Create();
        }

        void RenderMask()
        {
            if (_maskCamera == null || _maskRT == null) return;

            // Copy main camera settings
            _maskCamera.CopyFrom(_mainCamera);
            _maskCamera.enabled = false;
            _maskCamera.targetTexture = _maskRT;
            _maskCamera.cullingMask = sharpenLayers;
            _maskCamera.clearFlags = CameraClearFlags.SolidColor;
            _maskCamera.backgroundColor = Color.black;

            // Render with replacement shader that outputs white
            _maskCamera.RenderWithShader(
                Shader.Find("Hidden/ProSharpen/MaskWrite"), "RenderType");
        }
    }
}
