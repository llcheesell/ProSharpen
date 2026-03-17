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
// ============================================================================

#include "ProSharpenCommon.hlsl"

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

// 5x5 Gaussian approximation using two-pass 3x3
// For higher quality mode - uses wider neighborhood
float3 GaussianBlur5x5Approx(
    float3 samples[25], // 5x5 neighborhood, row-major
    float sigma)
{
    // Compute Gaussian weights for 5x5 kernel
    float weights[25];
    float totalWeight = 0.0;
    float invTwoSigmaSq = 1.0 / (2.0 * sigma * sigma);

    [unroll]
    for (int y = -2; y <= 2; y++)
    {
        [unroll]
        for (int x = -2; x <= 2; x++)
        {
            int idx = (y + 2) * 5 + (x + 2);
            float dist2 = float(x * x + y * y);
            weights[idx] = exp(-dist2 * invTwoSigmaSq);
            totalWeight += weights[idx];
        }
    }

    float3 result = float3(0, 0, 0);
    [unroll]
    for (int j = 0; j < 25; j++)
    {
        result += samples[j] * (weights[j] / totalWeight);
    }
    return result;
}

// Apply Unsharp Mask with artifact prevention
// amount: strength of sharpening (0-2 typical range)
// threshold: minimum difference to sharpen (noise gate)
// clampLimit: maximum adjustment to prevent halos
float3 ApplyUnsharpMask(float3 original, float3 blurred,
                        float amount, float threshold, float clampLimit)
{
    // Compute the detail signal (high-pass)
    float3 detail = original - blurred;

    // Threshold: suppress sharpening for small differences (noise)
    float detailLum = abs(Luminance709(detail));
    float thresholdMask = smoothstep(threshold * 0.5, threshold * 1.5, detailLum);

    // Apply amount with threshold gating
    float3 sharpDetail = detail * amount * thresholdMask;

    // Soft clamp to prevent halo artifacts
    sharpDetail = SoftClamp3(sharpDetail, clampLimit);

    return original + sharpDetail;
}

// Luminance-only USM: sharpen luminance, preserve chrominance
// This avoids color shifts that can occur with RGB sharpening
float3 ApplyUnsharpMaskLuma(float3 original, float3 blurred,
                            float amount, float threshold, float clampLimit)
{
    float lumOrig = Luminance709(original);
    float lumBlur = Luminance709(blurred);

    float detail = lumOrig - lumBlur;

    // Threshold gating
    float thresholdMask = smoothstep(threshold * 0.5, threshold * 1.5, abs(detail));

    // Apply sharpening in luminance
    float sharpDetail = detail * amount * thresholdMask;

    // Soft clamp
    sharpDetail = SoftClamp(sharpDetail, clampLimit);

    // Reconstruct: scale original color by luminance ratio
    float newLum = lumOrig + sharpDetail;
    float3 result = original * (newLum / max(lumOrig, 1e-6));

    // Handle near-black pixels where ratio would explode
    result = lumOrig < 1e-6 ? original : result;

    return max(result, 0.0);
}

#endif // PROSHARPEN_UNSHARP_MASK_INCLUDED
