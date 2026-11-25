#ifndef PROSHARPEN_RCAS_INCLUDED
#define PROSHARPEN_RCAS_INCLUDED

// ============================================================================
// Robust Contrast Adaptive Sharpening (RCAS)
// AMD FidelityFX FSR1 (ffx_fsr1.h) の RCAS に準拠
//   https://github.com/GPUOpen-Effects/FidelityFX-FSR
//
// 出力 = (lobe * (b+d+f+h) + e) / (4 * lobe + 1) が 0..1 をはみ出さない限界の
// lobe をチャンネルごとに解き、最も控えめな値を採用する。
// ============================================================================

#include "ProSharpenCommon.hlsl"

// lobe の下限。-0.25 で分母 (4 * lobe + 1) が 0 になるため、AMD と同じ余裕を取る
#define PS_RCAS_LIMIT (0.25 - (1.0 / 16.0))

// 孤立したノイズ画素ほど小さくなる係数 (1.0 〜 0.5)。FSR_RCAS_DENOISE 相当
float RCASDenoise(float lb, float ld, float le, float lf, float lh)
{
    float nz = 0.25 * (lb + ld + lf + lh) - le;
    float mx = max(le, max(max(lb, ld), max(lf, lh)));
    float mn = min(le, min(min(lb, ld), min(lf, lh)));
    nz = saturate(abs(nz) / max(mx - mn, 1e-5));
    return 1.0 - 0.5 * nz;
}

// mn4 / mx4 (十字 4 タップの min / max) から lobe を解く
float RCASLobe(PS_SIGNAL mn4, PS_SIGNAL mx4, PS_SIGNAL e, float sharpness)
{
    // 出力が 0 を下回らない限界と、1 を上回らない限界
    PS_SIGNAL hitMin = min(mn4, e) / (4.0 * mx4 + 1e-5);
    PS_SIGNAL hitMax = (1.0 - max(mx4, e)) / (4.0 * mn4 - 4.0 - 1e-5);
    PS_SIGNAL lobeSignal = max(-hitMin, hitMax);

    float lobe = max(-PS_RCAS_LIMIT, min(PSMaxComponent(lobeSignal), 0.0));
    return lobe * saturate(sharpness);
}

// Realtime: 十字 5 タップ。戻り値はリニア信号上の差分 (出力 - 中心)
PS_SIGNAL ApplyRCAS(PS_SIGNAL b, PS_SIGNAL d, PS_SIGNAL e, PS_SIGNAL f, PS_SIGNAL h,
                    float sharpness)
{
    PS_SIGNAL mn4 = min(min(b, d), min(f, h));
    PS_SIGNAL mx4 = max(max(b, d), max(f, h));

    float lobe = RCASLobe(mn4, mx4, e, sharpness);
    lobe *= RCASDenoise(PSLuma(b), PSLuma(d), PSLuma(e), PSLuma(f), PSLuma(h));

    return lobe * ((b + d + f + h) - 4.0 * e) / (4.0 * lobe + 1.0);
}

// High: 対角 4 タップを min/max 推定に加える (フィルタ核は十字のまま)
// 4 タップと 8 タップの境界を平均することで、斜めエッジでの過剰なシャープを抑える
PS_SIGNAL ApplyRCASHighQuality(PS_SIGNAL a, PS_SIGNAL b, PS_SIGNAL c,
                               PS_SIGNAL d, PS_SIGNAL e, PS_SIGNAL f,
                               PS_SIGNAL g, PS_SIGNAL h, PS_SIGNAL i,
                               float sharpness)
{
    PS_SIGNAL mn4 = min(min(b, d), min(f, h));
    PS_SIGNAL mx4 = max(max(b, d), max(f, h));
    PS_SIGNAL mn8 = min(mn4, min(min(a, c), min(g, i)));
    PS_SIGNAL mx8 = max(mx4, max(max(a, c), max(g, i)));

    float lobe = RCASLobe(0.5 * (mn4 + mn8), 0.5 * (mx4 + mx8), e, sharpness);
    lobe *= RCASDenoise(PSLuma(b), PSLuma(d), PSLuma(e), PSLuma(f), PSLuma(h));

    return lobe * ((b + d + f + h) - 4.0 * e) / (4.0 * lobe + 1.0);
}

#endif // PROSHARPEN_RCAS_INCLUDED
