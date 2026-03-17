Shader "Hidden/ProSharpen/ProSharpen"
{
    // ========================================================================
    // ProSharpen - Professional Sharpening Post-Process
    // Supports HDRP and URP with proper SRP conventions
    // ========================================================================

    HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_local _ PROSHARPEN_CAS PROSHARPEN_UNSHARP PROSHARPEN_ADAPTIVE PROSHARPEN_RCAS
        #pragma multi_compile_local _ PROSHARPEN_HIGH_QUALITY
        #pragma multi_compile_local _ PROSHARPEN_LUMA_ONLY
        #pragma multi_compile_local _ PROSHARPEN_DEPTH_AWARE
        #pragma multi_compile_local _ PROSHARPEN_MOTION_AWARE
        #pragma multi_compile_local _ PROSHARPEN_MASK_ENABLED

        #include "CAS.hlsl"
        #include "UnsharpMask.hlsl"
        #include "AdaptiveSharpen.hlsl"
        #include "RCAS.hlsl"

        // Parameters
        float _Intensity;
        float _Sharpness;
        float _EdgeProtection;
        float _DetailBoost;
        float _NoiseThreshold;
        float _ClampLimit;
        float _CornerWeight;

        // Depth-aware parameters
        float _DepthNearStrength;
        float _DepthFarStrength;
        float _DepthNearPlane;
        float _DepthFarPlane;

        // Motion-aware parameters
        float _MotionThreshold;
        float _MotionReduction;

        // Core sharpening logic shared by both pipelines
        float3 ProSharpenCore(float3 a, float3 b, float3 c,
                              float3 d, float3 e, float3 f,
                              float3 g, float3 h, float3 i)
        {
            float3 result = e;

            #if defined(PROSHARPEN_CAS)
            {
                #if defined(PROSHARPEN_HIGH_QUALITY)
                    result = ApplyCASHighQuality(a, b, c, d, e, f, g, h, i, _Sharpness, _CornerWeight);
                #else
                    result = ApplyCAS(a, b, c, d, e, f, g, h, i, _Sharpness);
                #endif
            }
            #elif defined(PROSHARPEN_UNSHARP)
            {
                float3 blurred = GaussianBlur3x3(a, b, c, d, e, f, g, h, i);
                #if defined(PROSHARPEN_LUMA_ONLY)
                    result = ApplyUnsharpMaskLuma(e, blurred, _Sharpness, _NoiseThreshold, _ClampLimit);
                #else
                    result = ApplyUnsharpMask(e, blurred, _Sharpness, _NoiseThreshold, _ClampLimit);
                #endif
            }
            #elif defined(PROSHARPEN_ADAPTIVE)
            {
                result = ApplyAdaptiveSharpen(a, b, c, d, e, f, g, h, i,
                    _Sharpness, _EdgeProtection, _DetailBoost);
            }
            #elif defined(PROSHARPEN_RCAS)
            {
                #if defined(PROSHARPEN_HIGH_QUALITY)
                    result = ApplyRCASHighQuality(a, b, c, d, e, f, g, h, i, _Sharpness);
                #else
                    result = ApplyRCAS(b, d, e, f, h, _Sharpness);
                #endif
            }
            #endif

            // Blend with intensity
            result = lerp(e, result, _Intensity);
            return max(result, 0.0);
        }
    ENDHLSL

    // ========================================================================
    // HDRP SubShader
    // Uses HDRP conventions: TEXTURE2D_X, GetFullScreenTriangleVertexPosition,
    // ClampAndScaleUVForBilinearPostProcessTexture for DLSS/DRS support
    // ========================================================================
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "ProSharpen_HDRP"
            ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D_X(_MainTex);
            float4 _MainTex_TexelSize;

            #ifdef PROSHARPEN_MOTION_AWARE
            TEXTURE2D_X(_CameraMotionVectorsTexture);
            #endif

            #ifdef PROSHARPEN_MASK_ENABLED
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            #endif

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

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Use HDRP's UV clamping for DLSS/dynamic resolution support
                float2 uv = ClampAndScaleUVForBilinearPostProcessTexture(input.texcoord);
                float2 texelSize = _MainTex_TexelSize.xy;

                // Sample 3x3 neighborhood
                float3 a, b, c, d, e, f, g, h, i;
                a = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv + float2(-texelSize.x,  texelSize.y)).rgb;
                b = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv + float2(0.0,           texelSize.y)).rgb;
                c = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv + float2( texelSize.x,  texelSize.y)).rgb;
                d = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv + float2(-texelSize.x,  0.0        )).rgb;
                e = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv).rgb;
                f = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv + float2( texelSize.x,  0.0        )).rgb;
                g = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv + float2(-texelSize.x, -texelSize.y)).rgb;
                h = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv + float2(0.0,          -texelSize.y)).rgb;
                i = SAMPLE_TEXTURE2D_X(_MainTex, s_linear_clamp_sampler, uv + float2( texelSize.x, -texelSize.y)).rgb;

                float3 result = ProSharpenCore(a, b, c, d, e, f, g, h, i);

                // Depth-aware modulation
                #ifdef PROSHARPEN_DEPTH_AWARE
                {
                    uint2 positionSS = input.texcoord * _ScreenSize.xy;
                    float depth = LoadCameraDepth(positionSS);
                    float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                    float normalizedDepth = saturate((linearDepth - _DepthNearPlane) / (_DepthFarPlane - _DepthNearPlane + 0.001));
                    float depthFactor = DepthSharpenFactor(normalizedDepth, 0.0, 1.0,
                        _DepthNearStrength, _DepthFarStrength);
                    result = lerp(e, result, depthFactor);
                }
                #endif

                // Motion-aware modulation (TAA/DLSS recovery)
                #ifdef PROSHARPEN_MOTION_AWARE
                {
                    uint2 positionSS = input.texcoord * _ScreenSize.xy;
                    float2 motionVec = LOAD_TEXTURE2D_X(_CameraMotionVectorsTexture, positionSS).xy;
                    float motionFactor = MotionSharpenFactor(motionVec, _MotionThreshold, _MotionReduction);
                    result = lerp(e, result, motionFactor);
                }
                #endif

                // Mask-based selective sharpening
                #ifdef PROSHARPEN_MASK_ENABLED
                {
                    float mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.texcoord).r;
                    result = lerp(e, result, mask);
                }
                #endif

                return float4(result, 1.0);
            }
            ENDHLSL
        }
    }

    // ========================================================================
    // URP SubShader
    // Uses Blit.hlsl conventions: _BlitTexture, Vert from Blit.hlsl
    // ========================================================================
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ProSharpen_URP"
            ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Vert and Varyings are provided by Blit.hlsl
            // _BlitTexture is the source texture, set by the Blitter API

            float4 _BlitTexture_TexelSize;

            #ifdef PROSHARPEN_DEPTH_AWARE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            #ifdef PROSHARPEN_MASK_ENABLED
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            #endif

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 texelSize = _BlitTexture_TexelSize.xy;

                // Sample 3x3 neighborhood using _BlitTexture from Blit.hlsl
                float3 a, b, c, d, e, f, g, h, i;
                a = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texelSize.x,  texelSize.y)).rgb;
                b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0,           texelSize.y)).rgb;
                c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( texelSize.x,  texelSize.y)).rgb;
                d = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texelSize.x,  0.0        )).rgb;
                e = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                f = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( texelSize.x,  0.0        )).rgb;
                g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texelSize.x, -texelSize.y)).rgb;
                h = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0,          -texelSize.y)).rgb;
                i = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( texelSize.x, -texelSize.y)).rgb;

                float3 result = ProSharpenCore(a, b, c, d, e, f, g, h, i);

                // Depth-aware modulation
                #ifdef PROSHARPEN_DEPTH_AWARE
                {
                    float depth = SampleSceneDepth(uv);
                    float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                    float normalizedDepth = saturate((linearDepth - _DepthNearPlane) / (_DepthFarPlane - _DepthNearPlane + 0.001));
                    float depthFactor = DepthSharpenFactor(normalizedDepth, 0.0, 1.0,
                        _DepthNearStrength, _DepthFarStrength);
                    result = lerp(e, result, depthFactor);
                }
                #endif

                // Mask-based selective sharpening
                #ifdef PROSHARPEN_MASK_ENABLED
                {
                    float mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv).r;
                    result = lerp(e, result, mask);
                }
                #endif

                return float4(result, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
