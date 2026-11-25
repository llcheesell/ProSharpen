#ifndef PROSHARPEN_UNSHARP_MASK_INCLUDED
#define PROSHARPEN_UNSHARP_MASK_INCLUDED

// ============================================================================
// Unsharp Mask (USM)
//
// 出力 = 元画像 + amount * (元画像 - ガウスぼかし)
//
// ぼかしはバイリニア補間を利用して少ないタップ数で分離型ガウスを再現する。
// (2 テクセルの混合はリニア空間で行われるため、硬いエッジでは知覚空間のガウスとわずかに差が出る)
//   Realtime: 3x3 = 9 タップで 5x5 ガウス相当
//   High    : 5x5 = 25 タップで 9x9 ガウス相当
// 隣り合う 2 テクセルの重み w1, w2 を、その重心 (w1*1 + w2*2)/(w1+w2) に置いた
// 1 回のバイリニアサンプルで取得する。オフセットと重みは C# 側で Radius から計算する。
//
// 知覚空間 (ガンマ 2.0) の信号で処理するため、明暗のハローが均等に出る。
// ハローの抑制は共通リミッタ (ProSharpenCore.hlsl) が担当する。
// ============================================================================

#include "ProSharpenCommon.hlsl"

// ユーザーの Sharpness [0, 1] を USM の Amount [0, 2.5] (0〜250%) に変換する
float USMRemapAmount(float sharpness)
{
    return sharpness * 2.5;
}

// 3x3 バイリニアタップからガウスぼかしを合成する
// w0 = 中心の 1 軸重み、w1 = 外側タップの 1 軸重み (w0 + 2 * w1 = 1)
PS_SIGNAL USMBlur3x3(PS_SIGNAL a, PS_SIGNAL b, PS_SIGNAL c,
                     PS_SIGNAL d, PS_SIGNAL e, PS_SIGNAL f,
                     PS_SIGNAL g, PS_SIGNAL h, PS_SIGNAL i,
                     float w0, float w1)
{
    return (w0 * w0) * e
         + (w0 * w1) * (b + d + f + h)
         + (w1 * w1) * (a + c + g + i);
}

#endif // PROSHARPEN_UNSHARP_MASK_INCLUDED
