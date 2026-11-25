Shader "Hidden/ProSharpen/ProSharpen"
{
    // ========================================================================
    // ProSharpen - プロ向けシャープニング ポストプロセス
    // HDRP / URP の両方に対応。アルゴリズム本体は ProSharpenCore.hlsl にある
    // ========================================================================

    HLSLINCLUDE
        #pragma target 4.5

        // モード。どれも有効でない場合は先頭の CAS が選ばれる
        #pragma multi_compile_local_fragment PROSHARPEN_CAS PROSHARPEN_UNSHARP PROSHARPEN_ADAPTIVE PROSHARPEN_RCAS
        #pragma multi_compile_local_fragment _ PROSHARPEN_HIGH_QUALITY
        #pragma multi_compile_local_fragment _ PROSHARPEN_LUMA_ONLY
        #pragma multi_compile_local_fragment _ PROSHARPEN_DEPTH_AWARE
        #pragma multi_compile_local_fragment _ PROSHARPEN_MOTION_AWARE
        #pragma multi_compile_local_fragment _ PROSHARPEN_MASK_ENABLED

        float _Intensity;

        // 深度連動
        float _DepthNearStrength;
        float _DepthFarStrength;
        float _DepthNearPlane;
        float _DepthFarPlane;

        // モーション連動
        float _MotionThreshold;
        float _MotionReduction;
    ENDHLSL

    // ========================================================================
    // HDRP SubShader
    // ========================================================================
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.high-definition" }
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

            // _CameraMotionVectorsTexture は HDRP の ShaderVariables.hlsl が宣言済み

            #ifdef PROSHARPEN_MASK_ENABLED
            TEXTURE2D(_MaskTex);
            #endif

            // ポストプロセス用バッファは RTHandle のため、ビューポート外を読まないよう
            // クランプしてからスケールを掛ける (動的解像度 / DLSS / FSR に対応)
            float4 ProSharpenSampleSource(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_MainTex, s_linear_clamp_sampler,
                    ClampAndScaleUVForBilinearPostProcessTexture(uv), 0);
            }

            #include "ProSharpenCore.hlsl"

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

                float2 uv = input.texcoord;
                float strength = _Intensity;

                // 深度とモーションベクターはアップスケール前の解像度 (_ScreenSize) で
                // 格納されているため、0..1 の UV から画素座標を求める
                #if defined(PROSHARPEN_DEPTH_AWARE) || defined(PROSHARPEN_MOTION_AWARE)
                uint2 positionSS = uint2(min(uv * _ScreenSize.xy, _ScreenSize.xy - 1.0));
                #endif

                #ifdef PROSHARPEN_DEPTH_AWARE
                {
                    float linearDepth = LinearEyeDepth(LoadCameraDepth(positionSS), _ZBufferParams);
                    strength *= DepthSharpenFactor(linearDepth, _DepthNearPlane, _DepthFarPlane,
                        _DepthNearStrength, _DepthFarStrength);
                }
                #endif

                #ifdef PROSHARPEN_MOTION_AWARE
                {
                    // HDRP はモーションベクター無効の画素を x > 1 で表す
                    float2 motion = LOAD_TEXTURE2D_X(_CameraMotionVectorsTexture, positionSS).xy;
                    motion = motion.x > 1.0 ? float2(0.0, 0.0) : motion;
                    strength *= MotionSharpenFactor(motion, _MotionThreshold, _MotionReduction);
                }
                #endif

                #ifdef PROSHARPEN_MASK_ENABLED
                strength *= SAMPLE_TEXTURE2D_LOD(_MaskTex, s_linear_clamp_sampler, uv, 0).r;
                #endif

                return ProSharpenApply(uv, _PostProcessScreenSize.zw, strength);
            }
            ENDHLSL
        }
    }

    // ========================================================================
    // URP SubShader
    // Blit.hlsl の規約 (_BlitTexture、Vert) を使用する
    // ========================================================================
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
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

            #ifdef PROSHARPEN_DEPTH_AWARE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            #ifdef PROSHARPEN_MASK_ENABLED
            TEXTURE2D(_MaskTex);
            #endif

            float4 ProSharpenSampleSource(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
            }

            #include "ProSharpenCore.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // 1 画素分の UV 幅は UV のスクリーン空間微分から得る。
                // _BlitTexture_TexelSize は URP のバージョンによって宣言の有無が異なるため使わない
                float2 texel = abs(float2(ddx(uv.x), ddy(uv.y)));

                float strength = _Intensity;

                #ifdef PROSHARPEN_DEPTH_AWARE
                {
                    float linearDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    strength *= DepthSharpenFactor(linearDepth, _DepthNearPlane, _DepthFarPlane,
                        _DepthNearStrength, _DepthFarStrength);
                }
                #endif

                #ifdef PROSHARPEN_MASK_ENABLED
                strength *= SAMPLE_TEXTURE2D_LOD(_MaskTex, sampler_LinearClamp, uv, 0).r;
                #endif

                return ProSharpenApply(uv, texel, strength);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
