#ifndef PROSHARPEN_CAS_INCLUDED
#define PROSHARPEN_CAS_INCLUDED

// ============================================================================
// Contrast Adaptive Sharpening (CAS)
// AMD FidelityFX CAS (ffx_cas.h) の式に準拠
//   https://github.com/GPUOpen-Effects/FidelityFX-CAS
//
// 近傍の min/max から「クリップせずにシャープできる余裕」を求め、その平方根を
// フィルタの負ローブに掛ける。入力は 0..1 のリニア信号を前提とする。
//
//  a b c
//  d e f    e = 中心画素
//  g h i
// ============================================================================

#include "ProSharpenCommon.hlsl"

// CAS の適応量 (0..1) を返す。mn / mx は十字 5 タップの輝度 min / max
float CASAmount(float mn, float mx)
{
    // 余裕 = min(黒までの距離, 白までの距離) / 最大値。HDR (mx > 1) では 0 になる
    float amp = saturate(min(mn, 1.0 - mx) / max(mx, 1e-5));
    return sqrt(amp);
}

// "better diagonals" 版。対角を含めた min/max を加算するため、白の基準が 2.0 になる
float CASAmountDiagonal(float mnCross, float mxCross, float mnAll, float mxAll)
{
    float mn = mnCross + mnAll;
    float mx = mxCross + mxAll;
    float amp = saturate(min(mn, 2.0 - mx) / max(mx, 1e-5));
    return sqrt(amp);
}

// フィルタの負ローブ重み。sharpness 0 → -1/8、1 → -1/5 (AMD の定義どおり)
// 1 + 4w が 0.2 以上に保たれるため、ゼロ除算は発生しない
float CASLobe(float amount, float sharpness)
{
    return -amount / lerp(8.0, 5.0, saturate(sharpness));
}

// Realtime: 十字 5 タップ。戻り値はリニア信号上の差分 (出力 - 中心)
PS_SIGNAL ApplyCAS(PS_SIGNAL b, PS_SIGNAL d, PS_SIGNAL e, PS_SIGNAL f, PS_SIGNAL h,
                   float sharpness)
{
    float lb = PSLuma(b), ld = PSLuma(d), le = PSLuma(e), lf = PSLuma(f), lh = PSLuma(h);
    float mn = min(le, min(min(lb, ld), min(lf, lh)));
    float mx = max(le, max(max(lb, ld), max(lf, lh)));

    float w = CASLobe(CASAmount(mn, mx), sharpness);

    // (w * (b+d+f+h) + e) / (1 + 4w) - e を整理した形
    return w * ((b + d + f + h) - 4.0 * e) / (1.0 + 4.0 * w);
}

// High: 3x3 の 9 タップ。対角を min/max 推定とフィルタ核の両方に使う
// cornerWeight は負ローブのうち対角へ配分する割合 (0 = 十字のみ)。
// 合計の負ローブ量は変えないため、分母は Realtime と同じ 1 + 4w になる
PS_SIGNAL ApplyCASHighQuality(PS_SIGNAL a, PS_SIGNAL b, PS_SIGNAL c,
                              PS_SIGNAL d, PS_SIGNAL e, PS_SIGNAL f,
                              PS_SIGNAL g, PS_SIGNAL h, PS_SIGNAL i,
                              float sharpness, float cornerWeight)
{
    float la = PSLuma(a), lb = PSLuma(b), lc = PSLuma(c);
    float ld = PSLuma(d), le = PSLuma(e), lf = PSLuma(f);
    float lg = PSLuma(g), lh = PSLuma(h), li = PSLuma(i);

    float mnCross = min(le, min(min(lb, ld), min(lf, lh)));
    float mxCross = max(le, max(max(lb, ld), max(lf, lh)));
    float mnAll = min(mnCross, min(min(la, lc), min(lg, li)));
    float mxAll = max(mxCross, max(max(la, lc), max(lg, li)));

    float w = CASLobe(CASAmountDiagonal(mnCross, mxCross, mnAll, mxAll), sharpness);

    float wCross = w / (1.0 + cornerWeight);
    float wDiag = wCross * cornerWeight;

    PS_SIGNAL hpCross = (b + d + f + h) - 4.0 * e;
    PS_SIGNAL hpDiag = (a + c + g + i) - 4.0 * e;
    return (wCross * hpCross + wDiag * hpDiag) / (1.0 + 4.0 * w);
}

#endif // PROSHARPEN_CAS_INCLUDED
