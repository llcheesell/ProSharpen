#ifndef PROSHARPEN_ADAPTIVE_SHARPEN_INCLUDED
#define PROSHARPEN_ADAPTIVE_SHARPEN_INCLUDED

// ============================================================================
// Adaptive Sharpening (方向性・エッジ適応シャープ)
//
// 3x3 近傍 (9 タップ) を知覚空間で解析し、画素ごとにシャープの掛け方を変える。
//   - 質感 (低〜中コントラストのディテール): Detail Boost で強める
//   - 強いエッジ (輪郭線、LED の画素境界など): Edge Protection でゲインを下げ、
//     さらにシャープの向きを「エッジを横切る方向」だけに絞る
//
// 等方的なラプラシアンはエッジに沿った方向の凹凸 (= ジャギーの階段) も増幅する。
// 勾配方向 n の 2 階微分 n^T H n だけを使うと、エッジの立ち上がりは鋭くしつつ
// 階段状のギザギザは増幅しない。
//
//  a b c
//  d e f    e = 中心画素
//  g h i
// ============================================================================

#include "ProSharpenCommon.hlsl"

// 解析のしきい値 (知覚空間の輝度差)
#define PS_ADAPTIVE_DIR_START    0.04   // これ以上の勾配から方向性シャープへ移行し始める
#define PS_ADAPTIVE_DIR_FULL     0.20   // これ以上の勾配では完全に方向性シャープ
#define PS_ADAPTIVE_EDGE_START   0.12   // これ以上の勾配からゲインを下げ始める
#define PS_ADAPTIVE_EDGE_FULL    0.50   // これ以上の勾配では Edge Protection を全量適用
#define PS_ADAPTIVE_TEX_START    0.04   // これ以下の局所コントラストは Detail Boost を全量適用
#define PS_ADAPTIVE_TEX_END      0.30   // これ以上の局所コントラストは Detail Boost なし

// 戻り値は知覚空間の信号上の差分 (出力 - 中心)。リミッタ適用前
PS_SIGNAL ApplyAdaptiveSharpen(PS_SIGNAL a, PS_SIGNAL b, PS_SIGNAL c,
                               PS_SIGNAL d, PS_SIGNAL e, PS_SIGNAL f,
                               PS_SIGNAL g, PS_SIGNAL h, PS_SIGNAL i,
                               float sharpness, float edgeProtection, float detailBoost)
{
    float la = PSLuma(a), lb = PSLuma(b), lc = PSLuma(c);
    float ld = PSLuma(d), le = PSLuma(e), lf = PSLuma(f);
    float lg = PSLuma(g), lh = PSLuma(h), li = PSLuma(i);

    // Sobel 勾配。0.25 を掛けると「段差の高さ」に相当する値になる
    float gx = (lc + 2.0 * lf + li) - (la + 2.0 * ld + lg);
    float gy = (la + 2.0 * lb + lc) - (lg + 2.0 * lh + li);
    float g2 = gx * gx + gy * gy;
    float gradient = 0.25 * sqrt(g2);
    float2 n = float2(gx, gy) * rsqrt(g2 + 1e-8);

    // 3x3 の局所コントラスト
    float mn = min(le, min(min(min(la, lb), min(lc, ld)), min(min(lf, lg), min(lh, li))));
    float mx = max(le, max(max(max(la, lb), max(lc, ld)), max(max(lf, lg), max(lh, li))));
    float range = mx - mn;

    // 等方ハイパス: 回転対称性の高い 9 点ラプラシアン (符号反転)
    PS_SIGNAL hpIso = (20.0 * e - 4.0 * (b + d + f + h) - (a + c + g + i)) / 6.0;

    // 方向性ハイパス: 勾配方向の 2 階微分 (符号反転)
    PS_SIGNAL sxx = d + f - 2.0 * e;
    PS_SIGNAL syy = b + h - 2.0 * e;
    PS_SIGNAL sxy = 0.25 * ((c + g) - (a + i));
    PS_SIGNAL hpDir = -(n.x * n.x * sxx + 2.0 * n.x * n.y * sxy + n.y * n.y * syy);

    // 強いエッジほど方向性ハイパスへ寄せる
    float dirWeight = edgeProtection * smoothstep(PS_ADAPTIVE_DIR_START, PS_ADAPTIVE_DIR_FULL, gradient);
    PS_SIGNAL highPass = lerp(hpIso, hpDir, dirWeight);

    // ゲイン: 質感は持ち上げ、強いエッジは抑える
    float textureGain = 1.0 + detailBoost * (1.0 - smoothstep(PS_ADAPTIVE_TEX_START, PS_ADAPTIVE_TEX_END, range));
    float edgeGain = 1.0 - edgeProtection * smoothstep(PS_ADAPTIVE_EDGE_START, PS_ADAPTIVE_EDGE_FULL, gradient);

    return highPass * (sharpness * textureGain * edgeGain);
}

#endif // PROSHARPEN_ADAPTIVE_SHARPEN_INCLUDED
