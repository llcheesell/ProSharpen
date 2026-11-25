Shader "Hidden/ProSharpen/MaskWrite"
{
    // 対象オブジェクトを白で描き、シャープ用のマスクを作るシェーダー。
    // HDRP では ProSharpenStencilMaskPass、それ以外では ProSharpenMaskGenerator が使用する。

    // ========================================================================
    // HDRP SubShader
    // カメラ相対レンダリングに対応した座標変換を行い、カメラ深度との比較で
    // 手前の物体に隠れた部分を除外する
    // ========================================================================
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.high-definition" }
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "ProSharpenMask_HDRP"
            ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // xy = マスクの 1 画素分の UV 幅、z = 深度テストの有効フラグ
            float4 _ProSharpenMaskParams;

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                if (_ProSharpenMaskParams.z > 0.5)
                {
                    // マスクはカメラと解像度が異なるため、UV 経由でカメラ深度の画素座標へ変換する
                    float2 uv = input.positionCS.xy * _ProSharpenMaskParams.xy;
                    uint2 positionSS = uint2(min(uv * _ScreenSize.xy, _ScreenSize.xy - 1.0));

                    float sceneDepth = LinearEyeDepth(LoadCameraDepth(positionSS), _ZBufferParams);
                    float fragmentDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);

                    // 解像度差による誤判定を避けるため、距離に比例した余裕を持たせる
                    clip(sceneDepth * 1.02 + 0.02 - fragmentDepth);
                }

                return float4(1.0, 1.0, 1.0, 1.0);
            }
            ENDHLSL
        }
    }

    // ========================================================================
    // 汎用 SubShader (Replacement Shader 用)
    // ========================================================================
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
