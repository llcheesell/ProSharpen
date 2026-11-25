#ifndef PROSHARPEN_COMMON_INCLUDED
#define PROSHARPEN_COMMON_INCLUDED

// ============================================================================
// ProSharpen Common - 全アルゴリズム共通のユーティリティ
//
// 処理の考え方:
//   1. 各アルゴリズムは「処理信号」上でシャープ量 (delta) を求める
//      - Luminance Only 有効時: 信号 = 輝度 (float)
//      - 無効時             : 信号 = RGB (float3)
//   2. delta は必ず知覚空間 (ガンマ 2.0) の値に揃えてから共通リミッタに通す
//   3. 知覚空間の中心画素へ加算し、リニアへ戻して出力する
// ============================================================================

// BT.709 輝度係数
static const float3 PS_LUMA_709 = float3(0.2126, 0.7152, 0.0722);

// 処理信号の型。輝度のみなら float、RGB なら float3
#if defined(PROSHARPEN_LUMA_ONLY)
    #define PS_SIGNAL float
#else
    #define PS_SIGNAL float3
#endif

// CAS / RCAS は AMD の仕様どおりリニア信号で解く。USM / Adaptive は知覚空間で解く
#if defined(PROSHARPEN_CAS) || defined(PROSHARPEN_RCAS)
    #define PS_SIGNAL_IS_LINEAR 1
#else
    #define PS_SIGNAL_IS_LINEAR 0
#endif

// リニア RGB → リニアの処理信号
PS_SIGNAL PSLinearSignal(float3 linearRGB)
{
#if defined(PROSHARPEN_LUMA_ONLY)
    return max(dot(linearRGB, PS_LUMA_709), 0.0);
#else
    return max(linearRGB, 0.0);
#endif
}

// リニア RGB → アルゴリズムが扱う処理信号 (モードに応じてリニア or 知覚空間)
PS_SIGNAL PSSignal(float3 linearRGB)
{
#if PS_SIGNAL_IS_LINEAR
    return PSLinearSignal(linearRGB);
#else
    return sqrt(PSLinearSignal(linearRGB));
#endif
}

// 処理信号 → 解析用の輝度 (スカラー)
float PSLuma(PS_SIGNAL s)
{
#if defined(PROSHARPEN_LUMA_ONLY)
    return s;
#else
    return dot(s, PS_LUMA_709);
#endif
}

// 処理信号の最大成分
float PSMaxComponent(PS_SIGNAL s)
{
#if defined(PROSHARPEN_LUMA_ONLY)
    return s;
#else
    return max(s.x, max(s.y, s.z));
#endif
}

// しきい値 t 付近で 0→1 に立ち上がる滑らかなゲート (t = 0 でも安全)
float PSSoftGate(float value, float t)
{
    float x = saturate((value - 0.5 * t) / max(t, 1e-6));
    return x * x * (3.0 - 2.0 * x);
}

// ----------------------------------------------------------------------------
// 非対称ソフトリミッタ (知覚空間)
//
// 出力が「近傍の最小値 - undershoot 〜 近傍の最大値 + overshoot」を超えないよう、
// シャープ量を滑らかに抑える。明るい側 (白ハロー) と暗い側 (黒ハロー) を別々に
// 制限できる。さらに maxDelta で 1 画素あたりの変化量の絶対上限を掛ける。
//
// ハードクランプではなく p=4 ノルムのソフトニーを使うため、制限値より十分小さい
// シャープ量はほぼそのまま通り、平坦に潰れた見た目になりにくい。
// ----------------------------------------------------------------------------
PS_SIGNAL PSLimitDelta(PS_SIGNAL delta, PS_SIGNAL center, PS_SIGNAL mn, PS_SIGNAL mx,
                       float overshoot, float undershoot, float maxDelta)
{
    PS_SIGNAL up   = min(max(mx - center, 0.0) + overshoot,  maxDelta);
    PS_SIGNAL down = min(max(center - mn, 0.0) + undershoot, maxDelta);
    PS_SIGNAL lim  = lerp(down, up, step(0.0, delta));

    PS_SIGNAL l2 = lim * lim;
    PS_SIGNAL d2 = delta * delta;
    return delta * lim * rsqrt(sqrt(l2 * l2 + d2 * d2) + 1e-10);
}

// 深度に応じたシャープ強度 (近距離 → 遠距離で補間)
float DepthSharpenFactor(float linearDepth, float nearPlane, float farPlane,
                         float nearStrength, float farStrength)
{
    float t = saturate((linearDepth - nearPlane) / max(farPlane - nearPlane, 1e-3));
    return lerp(nearStrength, farStrength, t);
}

// モーションに応じたシャープ強度。動いている領域ほど弱める
// (動きの大きい領域は TAA の履歴棄却でノイズ/エイリアスが出やすく、増幅を避ける)
float MotionSharpenFactor(float2 motionVector, float motionThreshold, float motionReduction)
{
    float motionMag = length(motionVector);
    return saturate(1.0 - smoothstep(0.0, motionThreshold, motionMag) * motionReduction);
}

#endif // PROSHARPEN_COMMON_INCLUDED
