#ifndef PROSHARPEN_CORE_INCLUDED
#define PROSHARPEN_CORE_INCLUDED

// ============================================================================
// ProSharpen Core - パイプライン共通のシャープ処理本体
//
// インクルードする側 (各 SubShader) で、事前に次の関数を定義しておくこと:
//   float4 ProSharpenSampleSource(float2 uv);
//     ソースカラーをバイリニア + クランプでサンプルする。uv は ProSharpenApply に
//     渡したものと同じ座標系。
// ============================================================================

#include "ProSharpenCommon.hlsl"
#include "CAS.hlsl"
#include "RCAS.hlsl"
#include "UnsharpMask.hlsl"
#include "AdaptiveSharpen.hlsl"

float _Sharpness;
float _EdgeProtection;
float _DetailBoost;
float _NoiseThreshold;
float _ClampLimit;
float _CornerWeight;
float _Overshoot;
float _Undershoot;

// USM のガウス核: x = 内側タップのオフセット [px], y = 外側タップのオフセット [px],
//                z = 内側タップの 1 軸重み,       w = 外側タップの 1 軸重み
float4 _BlurParams;

// uv      : 中心画素の UV
// texel   : 1 画素分の UV 幅
// strength: シャープ量に掛ける倍率 (Intensity、深度、モーション、マスクの積)
float4 ProSharpenApply(float2 uv, float2 texel, float strength)
{
    float4 source = ProSharpenSampleSource(uv);

    #define PS_TAP(ox, oy) PSSignal(ProSharpenSampleSource(uv + float2(ox, oy) * texel).rgb)

    PS_SIGNAL e = PSSignal(source.rgb);
    PS_SIGNAL delta;
    PS_SIGNAL mn;
    PS_SIGNAL mx;

#if defined(PROSHARPEN_UNSHARP)
    // ------------------------------------------------------------------ USM
    #if defined(PROSHARPEN_HIGH_QUALITY)
        // 5x5 バイリニアタップ = 9x9 ガウス相当
        float offsets[5] = { -_BlurParams.y, -_BlurParams.x, 0.0, _BlurParams.x, _BlurParams.y };
        float w0 = 1.0 - 2.0 * (_BlurParams.z + _BlurParams.w);
        float weights[5] = { _BlurParams.w, _BlurParams.z, w0, _BlurParams.z, _BlurParams.w };

        PS_SIGNAL blur = 0.0;
        mn = e;
        mx = e;
        [unroll] for (int ty = 0; ty < 5; ty++)
        {
            [unroll] for (int tx = 0; tx < 5; tx++)
            {
                PS_SIGNAL tap = e;
                if (tx != 2 || ty != 2)
                    tap = PS_TAP(offsets[tx], offsets[ty]);

                blur += (weights[tx] * weights[ty]) * tap;

                // リミッタ用の min/max は内側の 3x3 だけで取る
                if (abs(tx - 2) <= 1 && abs(ty - 2) <= 1)
                {
                    mn = min(mn, tap);
                    mx = max(mx, tap);
                }
            }
        }
    #else
        // 3x3 バイリニアタップ = 5x5 ガウス相当
        float o = _BlurParams.x;
        PS_SIGNAL a = PS_TAP(-o,  o), b = PS_TAP(0.0,  o), c = PS_TAP(o,  o);
        PS_SIGNAL d = PS_TAP(-o, 0.0),                     f = PS_TAP(o, 0.0);
        PS_SIGNAL g = PS_TAP(-o, -o), h = PS_TAP(0.0, -o), i = PS_TAP(o, -o);

        float w1 = _BlurParams.z;
        PS_SIGNAL blur = USMBlur3x3(a, b, c, d, e, f, g, h, i, 1.0 - 2.0 * w1, w1);

        mn = min(e, min(min(min(a, b), min(c, d)), min(min(f, g), min(h, i))));
        mx = max(e, max(max(max(a, b), max(c, d)), max(max(f, g), max(h, i))));
    #endif

    delta = (e - blur) * USMRemapAmount(_Sharpness);

#elif defined(PROSHARPEN_ADAPTIVE)
    // ------------------------------------------------------------- Adaptive
    PS_SIGNAL a = PS_TAP(-1.0,  1.0), b = PS_TAP(0.0,  1.0), c = PS_TAP(1.0,  1.0);
    PS_SIGNAL d = PS_TAP(-1.0,  0.0),                        f = PS_TAP(1.0,  0.0);
    PS_SIGNAL g = PS_TAP(-1.0, -1.0), h = PS_TAP(0.0, -1.0), i = PS_TAP(1.0, -1.0);

    delta = ApplyAdaptiveSharpen(a, b, c, d, e, f, g, h, i,
        _Sharpness, _EdgeProtection, _DetailBoost);

    mn = min(e, min(min(b, d), min(f, h)));
    mx = max(e, max(max(b, d), max(f, h)));

#else
    // ----------------------------------------------------------- CAS / RCAS
    PS_SIGNAL b = PS_TAP(0.0, 1.0), d = PS_TAP(-1.0, 0.0), f = PS_TAP(1.0, 0.0), h = PS_TAP(0.0, -1.0);

    #if defined(PROSHARPEN_HIGH_QUALITY)
        PS_SIGNAL a = PS_TAP(-1.0, 1.0), c = PS_TAP(1.0, 1.0), g = PS_TAP(-1.0, -1.0), i = PS_TAP(1.0, -1.0);
        #if defined(PROSHARPEN_RCAS)
            delta = ApplyRCASHighQuality(a, b, c, d, e, f, g, h, i, _Sharpness);
        #else
            delta = ApplyCASHighQuality(a, b, c, d, e, f, g, h, i, _Sharpness, _CornerWeight);
        #endif
    #else
        #if defined(PROSHARPEN_RCAS)
            delta = ApplyRCAS(b, d, e, f, h, _Sharpness);
        #else
            delta = ApplyCAS(b, d, e, f, h, _Sharpness);
        #endif
    #endif

    mn = min(e, min(min(b, d), min(f, h)));
    mx = max(e, max(max(b, d), max(f, h)));

    // リニア信号で解いた結果を知覚空間の差分へ変換し、以降の共通処理に揃える
    PS_SIGNAL sharpened = sqrt(max(e + delta, 0.0));
    e = sqrt(e);
    mn = sqrt(mn);
    mx = sqrt(mx);
    delta = sharpened - e;
#endif

    #undef PS_TAP

    // ノイズゲート (coring): 局所コントラストがしきい値未満の平坦部はシャープしない
#if !defined(PROSHARPEN_CAS)
    delta *= PSSoftGate(PSLuma(mx - mn), _NoiseThreshold);
#endif

    // 強度を掛けてから、ハロー制限を行う
    delta *= strength;
    delta = PSLimitDelta(delta, e, mn, mx, _Overshoot, _Undershoot, _ClampLimit);

    // 知覚空間で加算し、リニアへ戻す。輝度のみの場合は RGB へ同じ量を加える
    float3 perceptual = sqrt(max(source.rgb, 0.0)) + delta;
    perceptual = max(perceptual, 0.0);

    // 変化のない画素 (マスク外など) は入力をそのまま返す。負値や丸め誤差を持ち込まない
    float3 result = any(delta != 0.0) ? perceptual * perceptual : source.rgb;
    return float4(result, source.a);
}

#endif // PROSHARPEN_CORE_INCLUDED
