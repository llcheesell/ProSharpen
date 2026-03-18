#ifndef PROSHARPEN_UNSHARP_MASK_INCLUDED
#define PROSHARPEN_UNSHARP_MASK_INCLUDED

// ============================================================================
// Unsharp Mask (USM) with Edge-Aware Clamping
//
// Classic USM: output = original + amount * (original - blurred)
// Enhanced with:
//   - Luminance-space processing to avoid color shifts
//   - Edge-aware clamping to prevent halo artifacts
//   - Soft clamping for natural falloff
//   - Threshold to avoid amplifying noise
//
// Note: User-facing Sharpness [0,1] is remapped internally to [0, 2.5]
// to provide a perceptually meaningful range.
// ============================================================================

#include "ProSharpenCommon.hlsl"

// Remap user sharpness [0, 1] to USM-appropriate amount [0, 2.5]
float USMRemapAmount(float sharpness)
{
    return sharpness * 2.5;
}

// Gaussian-approximated blur using a 3x3 kernel
// Weights approximate a sigma ~0.85 Gaussian
float3 GaussianBlur3x3(float3 a, float3 b, float3 c,
                       float3 d, float3 e, float3 f,
                       float3 g, float3 h, float3 i)
{
    // 1 2 1
    // 2 4 2  / 16
    // 1 2 1
    return (a + c + g + i
          + 2.0 * (b + d + f + h)
          + 4.0 * e) / 16.0;
}

// Wider blur approximation for High Quality mode
// Uses 3x3 kernel with box-blur weighting for a broader spread
// Effectively approximates a ~5-texel-diameter blur from 9 taps
float3 GaussianBlurWide3x3(float3 a, float3 b, float3 c,
                           float3 d, float3 e, float3 f,
                           float3 g, float3 h, float3 i)
{
    // Equal weighting across 3x3 gives a wider effective blur
    // (equivalent to box filter, ~sigma 1.5)
    // This produces a larger detail signal than the Gaussian-weighted version
    return (a + b + c + d + e + f + g + h + i) / 9.0;
}

// Apply Unsharp Mask with artifact prevention
// amount: strength of sharpening (internally remapped from user [0,1])
// threshold: minimum difference to sharpen (noise gate)
// clampLimit: maximum adjustment to prevent halos
float3 ApplyUnsharpMask(float3 original, float3 blurred,
                        float amount, float threshold, float clampLimit)
{
    // Remap to effective USM range
    float effectiveAmount = USMRemapAmount(amount);

    // Compute the detail signal (high-pass)
    float3 detail = original - blurred;

    // Threshold: suppress sharpening for small differences (noise)
    float detailLum = abs(Luminance709(detail));
    float thresholdMask = smoothstep(threshold * 0.5, threshold * 1.5, detailLum);

    // Apply amount with threshold gating
    float3 sharpDetail = detail * effectiveAmount * thresholdMask;

    // Soft clamp to prevent halo artifacts
    sharpDetail = SoftClamp3(sharpDetail, clampLimit);

    return original + sharpDetail;
}

// Luminance-only USM: sharpen luminance, preserve chrominance
// This avoids color shifts that can occur with RGB sharpening
float3 ApplyUnsharpMaskLuma(float3 original, float3 blurred,
                            float amount, float threshold, float clampLimit)
{
    float effectiveAmount = USMRemapAmount(amount);

    float lumOrig = Luminance709(original);
    float lumBlur = Luminance709(blurred);

    float detail = lumOrig - lumBlur;

    // Threshold gating
    float thresholdMask = smoothstep(threshold * 0.5, threshold * 1.5, abs(detail));

    // Apply sharpening in luminance
    float sharpDetail = detail * effectiveAmount * thresholdMask;

    // Soft clamp
    sharpDetail = SoftClamp(sharpDetail, clampLimit);

    // Reconstruct: add detail uniformly (additive approach avoids color blow-out)
    float newLum = lumOrig + sharpDetail;
    // Clamp ratio to prevent extreme scaling on dark/bright pixels
    float ratio = clamp(newLum / max(lumOrig, 1e-5), 0.5, 2.0);
    float3 result = original * ratio;

    // Handle near-black pixels
    result = lumOrig < 1e-5 ? original : result;

    return max(result, 0.0);
}

#endif // PROSHARPEN_UNSHARP_MASK_INCLUDED
