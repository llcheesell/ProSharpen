# Custom Post-Processing Research: Unity HDRP & URP

**Last Updated:** 2026-03-17
**Scope:** Unity 2022.3+ (HDRP/URP 14+), Unity 6 (HDRP/URP 17+)

---

## Table of Contents

1. [HDRP Custom Post Process (HDRP 14+)](#1-hdrp-custom-post-process-hdrp-14)
2. [URP Custom Post Process (URP 14+)](#2-urp-custom-post-process-urp-14)
3. [Unity 6 Changes (HDRP/URP 17+)](#3-unity-6-changes-hdrpurp-17)
4. [Stencil / Layer-Based Masking](#4-stencil--layer-based-masking)
5. [Package Structure: Supporting Both HDRP and URP](#5-package-structure-supporting-both-hdrp-and-urp)
6. [Sources](#6-sources)

---

## 1. HDRP Custom Post Process (HDRP 14+)

### 1.1 Overview

HDRP provides a first-class custom post-processing system via the `CustomPostProcessVolumeComponent` base class. This system automatically integrates with the Volume framework, meaning effects are blended by volumes like built-in effects. This API is **unchanged between HDRP 14 and HDRP 17** (Unity 6).

### 1.2 Creating a CustomPostProcessVolumeComponent

A custom effect consists of two files:
- A **C# script** extending `CustomPostProcessVolumeComponent` + `IPostProcessComponent`
- A **full-screen HLSL shader**

**IMPORTANT:** The file name and the class name **must be identical** or Unity will not serialize the component correctly.

#### Complete C# Example (GrayScale)

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System;

[Serializable, VolumeComponentMenu("Post-processing/Custom/GrayScale")]
public sealed class GrayScale : CustomPostProcessVolumeComponent, IPostProcessComponent
{
    [Tooltip("Controls the intensity of the effect.")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

    Material m_Material;

    public bool IsActive() => m_Material != null && intensity.value > 0f;

    // Choose injection point
    public override CustomPostProcessInjectionPoint injectionPoint =>
        CustomPostProcessInjectionPoint.AfterPostProcess;

    public override void Setup()
    {
        if (Shader.Find("Hidden/Shader/GrayScale") != null)
            m_Material = new Material(Shader.Find("Hidden/Shader/GrayScale"));
    }

    public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
    {
        if (m_Material == null)
            return;

        m_Material.SetFloat("_Intensity", intensity.value);
        m_Material.SetTexture("_MainTex", source);
        HDUtils.DrawFullScreen(cmd, m_Material, destination);
    }

    public override void Cleanup() => CoreUtils.Destroy(m_Material);
}
```

**Key API methods:**
- `Setup()` - Called once when the effect is first loaded. Allocate materials here.
- `Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)` - Called each frame the effect is active.
- `Cleanup()` - Called when the effect is destroyed. Release materials here.
- `IsActive()` - Return false to skip the effect entirely.

**Note on rendering:** Use `HDUtils.DrawFullScreen(cmd, material, destination)` instead of `cmd.Blit()`. The HDRP documentation uses both patterns but `HDUtils.DrawFullScreen` is the canonical approach in HDRP 14+/17+.

#### Complete HLSL Shader Example (GrayScale)

```hlsl
Shader "Hidden/Shader/GrayScale"
{
    Properties
    {
        _MainTex("Main Texture", 2DArray) = "grey" {}
    }

    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/PostProcessing/Shaders/FXAA.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/PostProcessing/Shaders/RTUpscale.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord   : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    float _Intensity;
    TEXTURE2D_X(_MainTex);

    float4 CustomPostProcess(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        // For dynamic resolution / DLSS support:
        float2 uv = ClampAndScaleUVForBilinearPostProcessTexture(input.texcoord);

        float3 sourceColor = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv).xyz;
        float3 color = lerp(sourceColor, Luminance(sourceColor), _Intensity);
        return float4(color, 1);
    }

    ENDHLSL

    SubShader
    {
        Pass
        {
            Name "GrayScale"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment CustomPostProcess
                #pragma vertex Vert
            ENDHLSL
        }
    }
    Fallback Off
}
```

**Shader rules:**
- Do NOT use transparency, blend modes, or `clip()` -- HDRP uses render target pooling, so the destination buffer may contain garbage data.
- Use `TEXTURE2D_X()` and `SAMPLE_TEXTURE2D_X()` macros for XR/stereo support.
- Use `ClampAndScaleUVForBilinearPostProcessTexture()` for dynamic resolution and DLSS compatibility.
- The `Luminance()` function is available from `Color.hlsl`.

### 1.3 Injection Points

The `CustomPostProcessInjectionPoint` enum provides the following values:

| Injection Point | Description |
|---|---|
| `AfterOpaqueAndSky` | After opaque geometry and sky rendering, before fog. Access to color + depth buffers. |
| `BeforeTAA` | Before Temporal Anti-Aliasing. |
| `BeforePostProcess` | Before all built-in post-processing (bloom, color grading, etc.). |
| `AfterPostProcessBlurs` | After post-process blur effects. |
| `AfterPostProcess` | After all post-processing. Final output stage. |

### 1.4 Registering Custom Post-Process in HDRP Settings

1. Go to **Edit > Project Settings > Graphics** and select the **HDRP Global Settings** tab.
2. Scroll down to **Custom Post Process Orders**.
3. There is a list for each injection point. Click **+** at the bottom of the appropriate list.
4. Select your custom post-process class.

The effect will only execute if it is both registered here AND added to a Volume in the scene.

### 1.5 Accessing Depth Buffer, Normal Buffer, Stencil, Motion Vectors

#### Depth Buffer Access (Shader)

```hlsl
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

// In fragment shader:
uint2 positionSS = input.texcoord * _ScreenSize.xy;
float depth = LoadCameraDepth(positionSS);
float linearEyeDepth = LinearEyeDepth(depth, _ZBufferParams);
```

#### Normal Buffer Access (Shader)

```hlsl
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"

// In fragment shader:
float3 GetNormalWS(uint2 positionSS, float depth)
{
    float3 normalWS = float3(0, 0, 0);
    if (depth > 0.0f) // G-Buffer is NOT cleared in HDRP, check depth
    {
        NormalData normalData;
        DecodeFromNormalBuffer(positionSS, normalData);
        normalWS = normalData.normalWS;
    }
    return normalWS;
}
```

**Important:** The G-Buffer is not cleared in HDRP. Always check `depth > 0.0f` before reading normal data to avoid accessing invalid data from the previous frame.

#### Motion Vectors

Motion vectors are available via `_CameraMotionVectorsTexture` after they've been generated. They are fully available at the `BeforePreRefraction` custom pass injection point and later.

```hlsl
TEXTURE2D_X(_CameraMotionVectorsTexture);

float2 motionVector = LOAD_TEXTURE2D_X(_CameraMotionVectorsTexture, positionSS).xy;
```

#### Key HLSL Includes for HDRP

```hlsl
// Core
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// HDRP-specific
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"

// Post-processing utilities
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/PostProcessing/Shaders/FXAA.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/PostProcessing/Shaders/RTUpscale.hlsl"
```

### 1.6 Custom Editor (Optional)

```csharp
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor;

[CustomEditor(typeof(GrayScale))]
sealed class GrayScaleEditor : VolumeComponentEditor
{
    SerializedDataParameter m_Intensity;

    public override void OnEnable()
    {
        base.OnEnable();
        var o = new PropertyFetcher<GrayScale>(serializedObject);
        m_Intensity = Unpack(o.Find(x => x.intensity));
    }

    public override void OnInspectorGUI()
    {
        PropertyField(m_Intensity);
    }
}
```

---

## 2. URP Custom Post Process (URP 14+)

### 2.1 Overview

URP does **not** have a built-in `CustomPostProcessVolumeComponent` system like HDRP. Instead, custom post-processing is achieved through:
- **ScriptableRendererFeature** + **ScriptableRenderPass** (the main mechanism)
- Optionally combined with **VolumeComponent** for parameter blending

### 2.2 ScriptableRendererFeature + ScriptableRenderPass (URP 14 / Compatibility Mode)

#### Complete ScriptableRenderPass

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class ColorBlitPass : ScriptableRenderPass
{
    ProfilingSampler m_ProfilingSampler = new ProfilingSampler("ColorBlit");
    Material m_Material;
    RTHandle m_CameraColorTarget;
    float m_Intensity;

    public ColorBlitPass(Material material)
    {
        m_Material = material;
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public void SetTarget(RTHandle colorHandle, float intensity)
    {
        m_CameraColorTarget = colorHandle;
        m_Intensity = intensity;
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        ConfigureTarget(m_CameraColorTarget);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cameraData = renderingData.cameraData;
        if (cameraData.camera.cameraType != CameraType.Game)
            return;

        if (m_Material == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            m_Material.SetFloat("_Intensity", m_Intensity);
            Blitter.BlitCameraTexture(cmd, m_CameraColorTarget, m_CameraColorTarget, m_Material, 0);
        }
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }
}
```

#### Complete ScriptableRendererFeature

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class ColorBlitRendererFeature : ScriptableRendererFeature
{
    public Shader m_Shader;
    public float m_Intensity;

    Material m_Material;
    ColorBlitPass m_RenderPass = null;

    public override void Create()
    {
        m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
        m_RenderPass = new ColorBlitPass(m_Material);
    }

    // Enqueue the pass -- do NOT access render targets here
    public override void AddRenderPasses(ScriptableRenderer renderer,
                                         ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
            renderer.EnqueuePass(m_RenderPass);
    }

    // Access render targets here (URP 14+ requirement)
    public override void SetupRenderPasses(ScriptableRenderer renderer,
                                           in RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            // Declare required inputs
            m_RenderPass.ConfigureInput(ScriptableRenderPassInput.Color);
            m_RenderPass.SetTarget(renderer.cameraColorTargetHandle, m_Intensity);
        }
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
    }
}
```

**Critical URP 14+ change:** You **must** access `renderer.cameraColorTargetHandle` and `renderer.cameraDepthTargetHandle` in `SetupRenderPasses()`, NOT in `AddRenderPasses()`. This was changed when URP migrated to RTHandles.

### 2.3 Injection Points (RenderPassEvent)

Key `RenderPassEvent` values for post-processing:

| Event | Description |
|---|---|
| `BeforeRenderingOpaques` | Before opaque geometry |
| `AfterRenderingOpaques` | After opaque geometry |
| `BeforeRenderingSkybox` | Before skybox |
| `AfterRenderingSkybox` | After skybox |
| `BeforeRenderingTransparents` | Before transparent geometry |
| `AfterRenderingTransparents` | After transparent geometry |
| `BeforeRenderingPostProcessing` | Before built-in post-processing |
| `AfterRenderingPostProcessing` | After built-in post-processing |
| `AfterRendering` | After everything |

### 2.4 Accessing Camera Textures

#### ConfigureInput

Call `ConfigureInput()` to declare what textures the pass needs. URP uses this to determine whether to render via an intermediate texture.

```csharp
// Request specific textures
m_RenderPass.ConfigureInput(ScriptableRenderPassInput.Color);
m_RenderPass.ConfigureInput(ScriptableRenderPassInput.Depth);
m_RenderPass.ConfigureInput(ScriptableRenderPassInput.Normal);
m_RenderPass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
```

#### RTHandle Allocation

```csharp
public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
{
    var colorDesc = renderingData.cameraData.cameraTargetDescriptor;
    colorDesc.depthBufferBits = 0;

    RenderingUtils.ReAllocateIfNeeded(ref rtTempColor, colorDesc,
        name: "_TemporaryColorTexture");
}

// Release in Dispose
public void ReleaseTargets()
{
    rtTempColor?.Release();
}
```

#### Blitter API (URP 14+)

The `Blitter` API replaces the deprecated `cmd.Blit()`:

```csharp
// Blit with material
Blitter.BlitCameraTexture(cmd, source, destination, material, passIndex);

// Simple copy
Blitter.BlitCameraTexture(cmd, source, destination);
```

**Important:** The Blitter API requires **hand-coded shaders**. Shader Graph shaders are NOT compatible with the Blitter API.

### 2.5 URP Fullscreen Shader Example

```hlsl
Shader "CustomEffects/ColorBlit"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZWrite Off Cull Off

        Pass
        {
            Name "ColorBlitPass"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            // Vert is provided by Blit.hlsl
            // It outputs Varyings with positionCS and texcoord

            float _Intensity;

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // _BlitTexture is the source texture, set by the Blitter API
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                // Apply your effect here
                return color * float4(0, _Intensity, 0, 1);
            }
            ENDHLSL
        }
    }
}
```

**Note:** When using the Blitter API, the source texture is automatically bound as `_BlitTexture`. The vertex shader `Vert` and `Varyings` struct are provided by `Blit.hlsl`.

### 2.6 Accessing Depth and Normals in URP Shaders

```hlsl
// Depth
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

float depth = SampleSceneDepth(uv);
float linearDepth = LinearEyeDepth(depth, _ZBufferParams);

// Normals
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

float3 normalWS = SampleSceneNormals(uv);

// Opaque Texture
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

float3 sceneColor = SampleSceneColor(uv);
```

---

## 3. Unity 6 Changes (HDRP/URP 17+)

### 3.1 HDRP 17 - Custom Post Processing

**Good news:** `CustomPostProcessVolumeComponent` is **unchanged** in HDRP 17. The same API from HDRP 14 continues to work. The documentation for HDRP 17 shows the same patterns with no breaking changes to this API.

HDRP does NOT expose a `ScriptableRendererFeature` equivalent. Custom post-processing in HDRP continues to use `CustomPostProcessVolumeComponent` exclusively.

**Minor differences in HDRP 17:**
- HDRP 17 added the `hasAdditionalProperties` override to `VolumeComponentEditor` (for the custom editor).
- Spatial-Temporal Post-Processing (STP) upscaler was added as an alternative to TAAU.
- Bug fixes for custom post-processes not releasing correctly when switching HDRP assets.

### 3.2 URP 17 - Render Graph API (Major Change)

URP 17 introduces the **Render Graph API** as the primary way to write custom render passes. The previous `Execute()` / `OnCameraSetup()` approach is now called "Compatibility Mode."

**Key facts:**
- Unity no longer develops or improves the non-render-graph rendering path.
- Compatibility mode will be **removed in a future Unity version**.
- Existing projects opened in Unity 6 automatically enable compatibility mode.
- To disable compatibility mode: **Project Settings > Graphics > Render Graph > Compatibility Mode (Render Graph Disabled)** (uncheck).

#### Render Graph API: RecordRenderGraph

The new entry point is `RecordRenderGraph()` on `ScriptableRenderPass`:

```csharp
public class MyPostProcessPass : ScriptableRenderPass
{
    private Material m_Material;

    // PassData holds resources for the render pass
    private class PassData
    {
        public TextureHandle sourceTexture;
        public Material material;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph,
        ContextContainer frameData)
    {
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

        // Get the camera color texture
        TextureHandle sourceTexture = resourceData.activeColorTexture;
        if (!sourceTexture.IsValid()) return;

        // Create a temporary destination texture
        var desc = renderGraph.GetTextureDesc(sourceTexture);
        desc.name = "_TempPostProcess";
        TextureHandle destination = renderGraph.CreateTexture(desc);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(
            "My Post Process", out var passData))
        {
            passData.sourceTexture = sourceTexture;
            passData.material = m_Material;

            builder.UseTexture(passData.sourceTexture);
            builder.SetRenderAttachment(destination, 0);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, data.sourceTexture,
                    new Vector4(1, 1, 0, 0), data.material, 0);
            });
        }

        // Swap camera color to the destination
        resourceData.cameraColor = destination;
    }
}
```

#### Key Render Graph Concepts

- **`UniversalResourceData`**: Access camera textures (`activeColorTexture`, `activeDepthTexture`, `cameraDepthTexture`).
- **`UniversalCameraData`**: Access camera information (replaces `renderingData.cameraData`).
- **`TextureHandle`**: Reference to a render graph texture.
- **`AddRasterRenderPass<T>()`**: Standard approach for adding a raster render pass.
- **`AddUnsafePass<T>()`**: Fallback for passes that need `SetRenderTarget` or `CommandBuffer` access. Slower because render graph cannot optimize it.
- **`SetRenderFunc()`**: Must use a **static** method or lambda to avoid GC allocations.
- **`builder.UseTexture()`**: Declares a texture as read.
- **`builder.SetRenderAttachment()`**: Declares a texture as a render target.

#### AddUnsafePass (Compatibility Fallback)

For passes that are difficult to migrate:

```csharp
using (var builder = renderGraph.AddUnsafePass<PassData>("Unsafe Pass", out var passData))
{
    passData.textureToWrite = someTexture;

    builder.UseTexture(passData.textureToWrite, AccessFlags.Write);

    builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
    {
        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
        context.cmd.SetRenderTarget(data.textureToWrite);
        // ... legacy rendering code
    });
}
```

**Warning:** `AddUnsafePass` may result in slower rendering because the render graph system cannot optimize the pass (no pass merging, unnecessary GPU memory transfers).

### 3.3 Supporting Both Compatibility Mode and Render Graph

To support both Unity 2022.3 (no render graph) and Unity 6 (render graph), implement **both** `Execute()` and `RecordRenderGraph()` in your `ScriptableRenderPass`:

```csharp
public class MyPass : ScriptableRenderPass
{
    // Legacy path (Unity 2022.3 / URP 14)
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        // ... compatibility mode implementation
    }

    // Render Graph path (Unity 6 / URP 17+)
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        // ... render graph implementation
    }
}
```

URP will call `RecordRenderGraph` when render graph is enabled, and `Execute` when in compatibility mode.

### 3.4 Volume Framework Changes (Unity 6)

Custom `VolumeComponent` classes overriding the `Override` method must now explicitly set `VolumeParameter.overrideState` to `true` when parameter values change. This ensures proper default value resets.

### 3.5 Removed/Deprecated APIs

- `RenderTargetHandle` is **obsolete**. Use `RTHandle` instead.
- `cameraColorTarget` / `cameraDepthTarget` properties replaced by `cameraColorTargetHandle` / `cameraDepthTargetHandle`.
- `SHADER_QUALITY_LOW/MEDIUM/HIGH` and `SHADER_HINT_NICE_QUALITY` shader defines removed. Use `SHADER_API_MOBILE` or `SHADER_API_GLES` instead.

---

## 4. Stencil / Layer-Based Masking

### 4.1 HDRP Stencil Buffer

#### Available User Bits

HDRP reserves most stencil bits for internal use. Only **bits 6 and 7** are available for custom effects:

- `UserStencilUsage.UserBit0` (bit 6)
- `UserStencilUsage.UserBit1` (bit 7)

All other bits are HDRP-reserved and may be cleared or overwritten at any time during the frame.

#### Writing Stencil Values (HDRP Material)

In shader Properties and Stencil block:

```hlsl
Properties
{
    [HideInInspector] _StencilWriteMask("_StencilWriteMask", Float) = 0
}

// In SubShader Pass:
Stencil
{
    Ref 255        // or specific value
    Comp Always
    WriteMask [_StencilWriteMask]
    Pass Replace
}
```

In C# (e.g., in a CustomPass):

```csharp
stencilMaterial.SetInt("_StencilWriteMask", (int)UserStencilUsage.UserBit0);
```

#### HDRP Custom Pass for Writing Stencil

Use a **DrawRenderers Custom Pass** or a custom `CustomPass` subclass to render specific objects and write stencil values:

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

class StencilWritePass : CustomPass
{
    public LayerMask targetLayer;
    Material stencilMaterial;

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        stencilMaterial = new Material(Shader.Find("Hidden/StencilWrite"));
        stencilMaterial.SetInt("_StencilWriteMask", (int)UserStencilUsage.UserBit0);
    }

    protected override void Execute(CustomPassContext ctx)
    {
        // Option A: Render to main depth/stencil buffer
        CoreUtils.SetRenderTarget(ctx.cmd,
            ctx.cameraColorBuffer, ctx.cameraDepthBuffer);

        // Draw objects on the target layer with stencil-writing material
        var result = new RendererListDesc(
            new ShaderTagId("Forward"),
            ctx.cullingResults,
            ctx.hdCamera.camera)
        {
            rendererConfiguration = PerObjectData.None,
            renderQueueRange = RenderQueueRange.all,
            sortingCriteria = SortingCriteria.CommonOpaque,
            layerMask = targetLayer,
            overrideMaterial = stencilMaterial,
            overrideMaterialPassIndex = 0
        };

        CoreUtils.DrawRendererList(ctx.renderContext, ctx.cmd,
            ctx.renderContext.CreateRendererList(result));
    }

    protected override void Cleanup()
    {
        CoreUtils.Destroy(stencilMaterial);
    }
}
```

#### Custom Depth Buffer (Alternative: 8 Stencil Bits)

If you need more than 2 stencil bits, use the custom depth buffer, which has 8 stencil bits that HDRP does not use:

```csharp
protected override void Execute(CustomPassContext ctx)
{
    CoreUtils.SetRenderTarget(ctx.cmd,
        ctx.customDepthBuffer.Value, ClearFlag.DepthStencil);
    // Render objects to the custom depth buffer...
}
```

#### HDRP Custom Pass Injection Points

| Injection Point | What's Available |
|---|---|
| `BeforeRendering` | Depth buffer only (just cleared) |
| `AfterOpaqueDepthAndNormal` | Opaque depth + normals + roughness + motion vectors |
| `AfterOpaqueAndSky` | Opaque + sky color/depth. Fog renders after this. |
| `BeforePreRefraction` | Motion vectors are complete |
| `BeforeTransparent` | Opaque + pre-refraction transparent + SSR |
| `BeforePostProcess` | All HDR geometry, before post-processing |
| `AfterPostProcess` | After all post-processing (LDR) |

**Recommended approach for selective post-processing in HDRP:**
1. Add a `CustomPassVolume` at injection point `BeforePostProcess` or `AfterOpaqueDepthAndNormal`.
2. Use a `DrawRenderers` custom pass that renders target objects with a stencil-writing override material.
3. In your `CustomPostProcessVolumeComponent` shader, read the stencil to selectively apply the effect.

### 4.2 URP Stencil Masking

#### Approach 1: Render Objects Renderer Feature (Built-in)

URP includes a built-in **Render Objects** renderer feature that supports stencil:

1. Add a **Render Objects** renderer feature.
2. Set **Event** to `AfterRenderingOpaques`.
3. In **Filters**, set **Layer Mask** to your target layer.
4. Enable **Stencil**, set value (e.g., 1), Compare Function = `Always`, Pass = `Replace`.

Then in your post-processing pass, test against the stencil value.

#### Approach 2: Custom Mask Texture

Render target objects to a separate render texture, then use it as a mask in your post-processing shader:

```csharp
public class MaskRenderPass : ScriptableRenderPass
{
    RTHandle m_MaskTexture;
    List<ShaderTagId> m_ShaderTags = new() { new ShaderTagId("UniversalForward") };
    FilteringSettings m_FilteringSettings;

    public MaskRenderPass(LayerMask layerMask)
    {
        m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, layerMask);
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.colorFormat = RenderTextureFormat.R8; // Single channel mask
        RenderingUtils.ReAllocateIfNeeded(ref m_MaskTexture, desc, name: "_ObjectMask");

        ConfigureTarget(m_MaskTexture);
        ConfigureClear(ClearFlag.Color, Color.black);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get();

        var drawSettings = CreateDrawingSettings(m_ShaderTags, ref renderingData,
            SortingCriteria.CommonOpaque);
        // Use a simple white override material
        // drawSettings.overrideMaterial = whiteMaterial;

        context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings);

        // Make mask available globally
        cmd.SetGlobalTexture("_ObjectMask", m_MaskTexture);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }

    public void Release()
    {
        m_MaskTexture?.Release();
    }
}
```

### 4.3 Reading Stencil in Post-Processing Shaders

#### HDRP

The depth-stencil buffer may not be directly bound during `DrawFullScreen` in custom post-processors. To work around this, you can:

1. Use a CustomPass to copy stencil data to a separate texture.
2. Pass that texture to your custom post-process material.

#### URP

In URP, you can bind the depth-stencil buffer in your render pass via `ConfigureTarget()`:

```csharp
// In OnCameraSetup:
RTHandle cameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
ConfigureTarget(m_ColorTarget, cameraDepth);
```

Then in the shader:

```hlsl
// Read stencil via depth texture (platform dependent, not always reliable)
// Recommended: use a mask texture approach instead.
```

---

## 5. Package Structure: Supporting Both HDRP and URP

### 5.1 Assembly Definition with Version Defines

Use Assembly Definition files (`.asmdef`) with `versionDefines` to conditionally compile code based on which render pipeline package is installed.

#### Example .asmdef File

```json
{
    "name": "MyPackage.Runtime",
    "rootNamespace": "MyPackage",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [
        {
            "name": "com.unity.render-pipelines.high-definition",
            "expression": "14.0.0",
            "define": "USING_HDRP"
        },
        {
            "name": "com.unity.render-pipelines.universal",
            "expression": "14.0.0",
            "define": "USING_URP"
        },
        {
            "name": "com.unity.render-pipelines.high-definition",
            "expression": "17.0.0",
            "define": "USING_HDRP_17_OR_NEWER"
        },
        {
            "name": "com.unity.render-pipelines.universal",
            "expression": "17.0.0",
            "define": "USING_URP_17_OR_NEWER"
        },
        {
            "name": "com.unity.render-pipelines.core",
            "expression": "14.0.0",
            "define": "USING_SRP"
        }
    ],
    "noEngineReferences": false
}
```

**How it works:** When the specified package is installed at or above the given version, the `define` symbol is automatically added during compilation of code within that assembly. This is compile-time, not runtime.

### 5.2 Conditional Compilation in C#

```csharp
using UnityEngine;
using UnityEngine.Rendering;

#if USING_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

#if USING_URP
using UnityEngine.Rendering.Universal;
#endif

public class MyEffect
{
#if USING_HDRP
    // HDRP-specific implementation using CustomPostProcessVolumeComponent
    [Serializable, VolumeComponentMenu("Post-processing/Custom/MyEffect")]
    public sealed class MyEffectHDRP : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        // ...
    }
#endif

#if USING_URP
    // URP-specific implementation using ScriptableRendererFeature
    public class MyEffectURPFeature : ScriptableRendererFeature
    {
        // ...
    }
#endif
}
```

### 5.3 Assembly References with Define Constraints

For pipeline-specific assemblies that should only compile when the pipeline is present:

#### HDRP-specific assembly (MyPackage.HDRP.Runtime.asmdef)

```json
{
    "name": "MyPackage.HDRP.Runtime",
    "references": [
        "MyPackage.Runtime",
        "Unity.RenderPipelines.Core.Runtime",
        "Unity.RenderPipelines.HighDefinition.Runtime"
    ],
    "defineConstraints": [],
    "versionDefines": [
        {
            "name": "com.unity.render-pipelines.high-definition",
            "expression": "14.0.0",
            "define": "USING_HDRP"
        }
    ]
}
```

#### URP-specific assembly (MyPackage.URP.Runtime.asmdef)

```json
{
    "name": "MyPackage.URP.Runtime",
    "references": [
        "MyPackage.Runtime",
        "Unity.RenderPipelines.Core.Runtime",
        "Unity.RenderPipelines.Universal.Runtime"
    ],
    "defineConstraints": [],
    "versionDefines": [
        {
            "name": "com.unity.render-pipelines.universal",
            "expression": "14.0.0",
            "define": "USING_URP"
        }
    ]
}
```

### 5.4 Recommended Package Folder Structure

```
MyPackage/
  package.json
  Runtime/
    MyPackage.Runtime.asmdef           # Shared/core code
    Core/
      SharedSettings.cs                # Pipeline-agnostic settings
  Runtime.HDRP/
    MyPackage.HDRP.Runtime.asmdef      # HDRP-only assembly
    HDRPPostProcess.cs                 # CustomPostProcessVolumeComponent
    Shaders/
      HDRPEffect.shader
  Runtime.URP/
    MyPackage.URP.Runtime.asmdef       # URP-only assembly
    URPRendererFeature.cs              # ScriptableRendererFeature
    URPRenderPass.cs                   # ScriptableRenderPass
    Shaders/
      URPEffect.shader
  Editor/
    MyPackage.Editor.asmdef
  Editor.HDRP/
    MyPackage.HDRP.Editor.asmdef       # HDRP editor (VolumeComponentEditor)
  Editor.URP/
    MyPackage.URP.Editor.asmdef        # URP editor
```

### 5.5 Runtime Pipeline Detection

For runtime detection (e.g., in shared code that needs to behave differently):

```csharp
using UnityEngine.Rendering;

public static class PipelineUtils
{
    public static bool IsHDRP =>
        GraphicsSettings.currentRenderPipeline != null &&
        GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("HDRenderPipelineAsset");

    public static bool IsURP =>
        GraphicsSettings.currentRenderPipeline != null &&
        GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("UniversalRenderPipelineAsset");

    public static bool IsBuiltIn =>
        GraphicsSettings.currentRenderPipeline == null;
}
```

### 5.6 Shader Compatibility

HDRP and URP shaders are fundamentally different (different includes, different macros, different vertex/fragment conventions). You will need **separate shaders** for each pipeline. However, shared HLSL logic can be factored into `.hlsl` include files:

```
Shaders/
  Shared/
    MyEffectCommon.hlsl    # Shared math/logic
  HDRP/
    MyEffect_HDRP.shader   # HDRP full-screen shader
  URP/
    MyEffect_URP.shader     # URP Blit-compatible shader
```

---

## 6. Sources

### Official Unity Documentation
- [HDRP 14.0 Custom Post-Processing](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@14.0/manual/Custom-Post-Process.html)
- [HDRP 17.0 Custom Post-Processing Example Scripts](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/custom-post-processing-scripts.html)
- [HDRP 17.0 Create and Apply Custom Post-Processing](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.1/manual/custom-post-processing-create-apply.html)
- [HDRP 17.0 Stencil Buffer Usage](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Stencil-Usage.html)
- [HDRP 14.0 Custom Pass Injection Points](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@14.0/manual/Custom-Pass-Injection-Points.html)
- [HDRP 17.0 What's New](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/whats-new-17.html)
- [URP 14.0 Full Screen Blit Example](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/renderer-features/how-to-fullscreen-blit.html)
- [URP 14.0 Upgrade Guide (Unity 2022.2)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/upgrade-guide-2022-2.html)
- [URP 17 Upgrade Guide (Unity 6)](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-unity-6.html)
- [URP Render Graph - Write a Render Pass](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph-write-render-pass.html)
- [URP Render Graph - Unsafe Pass](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph-unsafe-pass.html)
- [URP Render Graph Introduction](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph-introduction.html)
- [URP 17 What's New](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/whats-new/urp-whats-new.html)
- [Unity Conditional Compilation](https://docs.unity3d.com/6000.2/Documentation/Manual/platform-dependent-compilation.html)
- [Unity Assembly Definitions and Packages](https://docs.unity3d.com/6000.2/Documentation/Manual/cus-asmdef.html)
- [URP Compatibility Mode in Render Graph](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/renderer-features/how-to-fullscreen-blit.html)

### Community Resources and Tutorials
- [Cyanilux - Custom Renderer Features (URP)](https://www.cyanilux.com/tutorials/custom-renderer-features/)
- [Febucci - Custom Post Processing in Unity URP](https://blog.febucci.com/2022/05/custom-post-processing-in-urp/)
- [Mitch McClellan - Depth Based Post Processing in HDRP](https://mitchmcclellan.com/depth-based-post-processing-in-hdrp/)
- [Antoine Huot - Unity HDRP G-Buffer for Post-Processing](https://antoinehuot.io/2025/03/01/unity-hdrp-g-buffer-for-post-processing/)
- [unitycoder - Detect SRP with Assembly Definition Version Defines](https://unitycoder.com/blog/2025/05/02/detect-srp-urp-or-hdrp-with-assemble-definition-version-defines/)
- [The Knights of U - Render Graph Introduction](https://theknightsofu.com/render-graph-introduction/)
- [Render Graph Updates in Unity 6.3 (Discussion)](https://discussions.unity.com/t/render-graph-updates-in-unity-6-3/1668122)

### GitHub Repositories
- [alelievr/HDRP-Custom-Passes](https://github.com/alelievr/HDRP-Custom-Passes) - Collection of HDRP custom passes including stencil masking examples
- [egomeh/stencil-buffer-post-process](https://github.com/egomeh/stencil-buffer-post-process) - Stencil buffer for selective post-processing
- [Deinol/URPStencilBuffer](https://github.com/Deinol/URPStencilBuffer) - URP stencil buffer example
- [unitycoder/SRPDetectionConditionalCompiling](https://github.com/unitycoder/SRPDetectionConditionalCompiling) - SRP detection via version defines
- [Unity-Technologies/Graphics](https://github.com/Unity-Technologies/Graphics) - Official Unity Graphics repository (SRP source)
