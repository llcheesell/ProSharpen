Shader "Test/Toon"
{
    // 検証用の簡易トゥーンシェーダー (2 階調 + ハードスペキュラ + 背面法アウトライン)
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _Shade("Shade", Color) = (0.6, 0.6, 0.75, 1)
        _OutlineColor("Outline", Color) = (0.03, 0.02, 0.04, 1)
        _OutlineWidth("Outline Width (px)", Float) = 2
        _Spec("Spec", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 vulkan metal
    #pragma multi_compile_instancing

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"

    float4 _Color, _Shade, _OutlineColor;
    float _OutlineWidth, _Spec;

    struct Attributes
    {
        float3 positionOS : POSITION;
        float3 normalOS : NORMAL;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 normalWS : TEXCOORD0;
        float3 viewWS : TEXCOORD1;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        float3 positionWS = TransformObjectToWorld(input.positionOS);
        output.positionCS = TransformWorldToHClip(positionWS);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        output.viewWS = GetWorldSpaceNormalizeViewDir(positionWS);
        return output;
    }

    Varyings VertOutline(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        float3 positionWS = TransformObjectToWorld(input.positionOS);
        float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
        float4 positionCS = TransformWorldToHClip(positionWS);
        float2 dirCS = TransformWorldToHClipDir(normalWS).xy;
        dirCS = dirCS * rsqrt(dot(dirCS, dirCS) + 1e-8);
        positionCS.xy += dirCS * _OutlineWidth * 2.0 * _ScreenSize.zw * positionCS.w;
        output.positionCS = positionCS;
        output.normalWS = normalWS;
        output.viewWS = 0;
        return output;
    }

    float4 FragToon(Varyings input) : SV_Target
    {
        float3 n = normalize(input.normalWS);
        float3 l = normalize(float3(0.45, 0.7, -0.55));
        float nl = dot(n, l) - 0.08;
        float t = saturate(nl / max(fwidth(nl), 1e-5) + 0.5);
        float3 color = lerp(_Shade.rgb * _Color.rgb, _Color.rgb, t);
        float3 h = normalize(l + normalize(input.viewWS));
        color += _Spec * step(0.985, dot(n, h));
        color = saturate(color);
        return float4(color * GetCurrentExposureMultiplier(), 1.0);
    }

    float4 FragOutline(Varyings input) : SV_Target
    {
        return float4(_OutlineColor.rgb * GetCurrentExposureMultiplier(), 1.0);
    }

    void FragDepth(Varyings input, out float4 outNormal : SV_Target0)
    {
        NormalData normalData;
        normalData.normalWS = normalize(input.normalWS);
        normalData.perceptualRoughness = 1.0;
        EncodeIntoNormalBuffer(normalData, outNormal);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragToon
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex VertOutline
            #pragma fragment FragOutline
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            ENDHLSL
        }
    }
}
