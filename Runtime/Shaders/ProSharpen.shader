Shader "Hidden/ProSharpen/ProSharpen"
{
    HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_local _ PROSHARPEN_CAS PROSHARPEN_UNSHARP PROSHARPEN_ADAPTIVE
        #pragma multi_compile_local _ PROSHARPEN_HIGH_QUALITY
        #pragma multi_compile_local _ PROSHARPEN_LUMA_ONLY
        #pragma multi_compile_local _ PROSHARPEN_DEPTH_AWARE
        #pragma multi_compile_local _ PROSHARPEN_MOTION_AWARE
        #pragma multi_compile_local _ PROSHARPEN_MASK_ENABLED

        #include "CAS.hlsl"
        #include "UnsharpMask.hlsl"
        #include "AdaptiveSharpen.hlsl"

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

        // Textures - set by pipeline-specific C# code
        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        float4 _MainTex_TexelSize;

        #ifdef PROSHARPEN_DEPTH_AWARE
        TEXTURE2D(_DepthTex);
        SAMPLER(sampler_DepthTex);
        #endif

        #ifdef PROSHARPEN_MOTION_AWARE
        TEXTURE2D(_MotionVectorTex);
        SAMPLER(sampler_MotionVectorTex);
        #endif

        #ifdef PROSHARPEN_MASK_ENABLED
        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);
        #endif

        struct Attributes
        {
            uint vertexID : SV_VertexID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 texcoord   : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            // Full-screen triangle
            output.positionCS = float4(
                input.vertexID == 1 ? 3.0 : -1.0,
                input.vertexID == 2 ? 3.0 : -1.0,
                0.0, 1.0
            );
            output.texcoord = float2(
                input.vertexID == 1 ? 2.0 : 0.0,
                input.vertexID == 2 ? 2.0 : 0.0
            );
            // Handle platform differences in UV direction
            #if UNITY_UV_STARTS_AT_TOP
            output.texcoord.y = 1.0 - output.texcoord.y;
            #endif
            return output;
        }

        // Sample the 3x3 neighborhood
        void Sample3x3(float2 uv, float2 texelSize,
                       out float3 a, out float3 b, out float3 c,
                       out float3 d, out float3 e, out float3 f,
                       out float3 g, out float3 h, out float3 i)
        {
            a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texelSize.x,  texelSize.y)).rgb;
            b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(          0.0,  texelSize.y)).rgb;
            c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texelSize.x,  texelSize.y)).rgb;
            d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texelSize.x,          0.0)).rgb;
            e = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
            f = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texelSize.x,          0.0)).rgb;
            g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texelSize.x, -texelSize.y)).rgb;
            h = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(          0.0, -texelSize.y)).rgb;
            i = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texelSize.x, -texelSize.y)).rgb;
        }

        float4 Frag(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float2 texelSize = _MainTex_TexelSize.xy;

            // Sample 3x3 neighborhood
            float3 a, b, c, d, e, f, g, h, i;
            Sample3x3(uv, texelSize, a, b, c, d, e, f, g, h, i);

            float3 result = e; // default: no sharpening

            // Apply selected sharpening algorithm
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
            #endif

            // Depth-aware modulation
            #ifdef PROSHARPEN_DEPTH_AWARE
            {
                float depth = SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, uv).r;
                float depthFactor = DepthSharpenFactor(depth, _DepthNearPlane, _DepthFarPlane,
                    _DepthNearStrength, _DepthFarStrength);
                result = lerp(e, result, depthFactor);
            }
            #endif

            // Motion-aware modulation (for TAA/DLSS recovery)
            #ifdef PROSHARPEN_MOTION_AWARE
            {
                float2 motionVec = SAMPLE_TEXTURE2D(_MotionVectorTex, sampler_MotionVectorTex, uv).rg;
                float motionFactor = MotionSharpenFactor(motionVec, _MotionThreshold, _MotionReduction);
                result = lerp(e, result, motionFactor);
            }
            #endif

            // Mask-based selective sharpening
            #ifdef PROSHARPEN_MASK_ENABLED
            {
                float mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv).r;
                result = lerp(e, result, mask);
            }
            #endif

            // Blend with intensity
            result = lerp(e, result, _Intensity);

            return float4(max(result, 0.0), 1.0);
        }
    ENDHLSL

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
            ENDHLSL
        }
    }

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
            ENDHLSL
        }
    }

    Fallback Off
}
