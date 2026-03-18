#ifndef PROSHARPEN_ADAPTIVE_SHARPEN_INCLUDED
#define PROSHARPEN_ADAPTIVE_SHARPEN_INCLUDED

// ============================================================================
// Adaptive Sharpening
//
// Combines multiple strategies for professional-quality sharpening:
//   - Local contrast analysis to modulate sharpening strength
//   - Edge detection to avoid over-sharpening edges (prevents jaggies)
//   - Depth-aware sharpening (when depth buffer is available)
//   - Motion-aware sharpening (when motion vectors are available)
//     for TAA/DLSS recovery
// ============================================================================

#include "ProSharpenCommon.hlsl"

// Estimate local contrast from a 3x3 neighborhood
// Returns a value [0,1] where 0 = flat area, 1 = high contrast
float LocalContrast3x3(float lumA, float lumB, float lumC,
                       float lumD, float lumE, float lumF,
                       float lumG, float lumH, float lumI)
{
    float minLum = min(lumE, min(min(min(lumA, lumB), min(lumC, lumD)),
                                min(min(lumF, lumG), min(lumH, lumI))));
    float maxLum = max(lumE, max(max(max(lumA, lumB), max(lumC, lumD)),
                                max(max(lumF, lumG), max(lumH, lumI))));

    // Michelson contrast
    float range = maxLum - minLum;
    float mean = (maxLum + minLum) * 0.5;
    return range / (mean + 0.05);
}

// Sobel-based edge strength
// Returns gradient magnitude normalized to [0,1]
float EdgeStrength3x3(float lumA, float lumB, float lumC,
                      float lumD, float lumE, float lumF,
                      float lumG, float lumH, float lumI)
{
    // Sobel X: -1 0 1 / -2 0 2 / -1 0 1
    float gx = (-lumA + lumC - 2.0 * lumD + 2.0 * lumF - lumG + lumI);
    // Sobel Y: -1 -2 -1 / 0 0 0 / 1 2 1
    float gy = (-lumA - 2.0 * lumB - lumC + lumG + 2.0 * lumH + lumI);

    return saturate(sqrt(gx * gx + gy * gy));
}

// Core adaptive sharpening
// Adjusts sharpening based on:
//   - Local contrast: sharpen more in low-contrast areas (detail recovery)
//   - Edge strength: sharpen less on strong edges (avoid jaggies)
float3 ApplyAdaptiveSharpen(
    float3 a, float3 b, float3 c,
    float3 d, float3 e, float3 f,
    float3 g, float3 h, float3 i,
    float intensity,        // Overall sharpening strength [0, 1]
    float edgeProtection,   // How much to protect edges [0, 1]
    float detailBoost)      // Extra boost for fine detail [0, 1]
{
    // Compute luminance for analysis
    float lumA = Luminance709(a);
    float lumB = Luminance709(b);
    float lumC = Luminance709(c);
    float lumD = Luminance709(d);
    float lumE = Luminance709(e);
    float lumF = Luminance709(f);
    float lumG = Luminance709(g);
    float lumH = Luminance709(h);
    float lumI = Luminance709(i);

    // Analyze local characteristics
    float contrast = LocalContrast3x3(lumA, lumB, lumC, lumD, lumE, lumF, lumG, lumH, lumI);
    float edge = EdgeStrength3x3(lumA, lumB, lumC, lumD, lumE, lumF, lumG, lumH, lumI);

    // Adaptive strength:
    // - In flat/low-contrast areas: sharpen more to recover detail
    // - On strong edges: reduce sharpening to avoid jaggies
    float contrastMod = lerp(1.0 + detailBoost, 1.0 - detailBoost * 0.5, saturate(contrast));
    float edgeMod = lerp(1.0, 1.0 - edgeProtection, saturate(edge * 2.0));
    float adaptiveStrength = intensity * contrastMod * edgeMod;

    // Compute sharpening using a Laplacian-like kernel in luminance
    // Cross kernel: -1 at neighbors, +4 at center (high-pass)
    float lumHighPass = 4.0 * lumE - (lumB + lumD + lumF + lumH);

    // Include diagonals with reduced weight for smoother result
    float lumHighPassFull = 8.0 * lumE - (lumB + lumD + lumF + lumH)
                          - 0.5 * (lumA + lumC + lumG + lumI);
    lumHighPassFull /= 6.0;

    // Blend between cross-only and full kernel based on edge presence
    float highPass = lerp(lumHighPassFull, lumHighPass * 0.25, saturate(edge));

    // Apply with adaptive strength
    float sharpDetail = highPass * adaptiveStrength;

    // Soft clamp to prevent artifacts
    float clampRange = 0.1 * intensity;
    sharpDetail = SoftClamp(sharpDetail, clampRange);

    // Apply in luminance space with ratio clamping to prevent bloating
    float newLum = lumE + sharpDetail;
    // Clamp the luminance ratio to prevent excessive brightening/darkening
    float ratio = clamp(newLum / max(lumE, 1e-5), 0.75, 1.25);
    float3 result = lumE < 1e-5 ? e : e * ratio;

    // Strict neighborhood clamping (no overshoot margin to prevent "swollen" look)
    float3 minColor = min(e, min(min(b, d), min(f, h)));
    float3 maxColor = max(e, max(max(b, d), max(f, h)));
    result = clamp(result, minColor, maxColor);

    return max(result, 0.0);
}

// Depth-aware sharpening modifier
// Returns a multiplier based on depth to sharpen foreground more
float DepthSharpenFactor(float depth, float nearPlane, float farPlane,
                         float nearStrength, float farStrength)
{
    float linearDepth = saturate((depth - nearPlane) / (farPlane - nearPlane));
    return lerp(nearStrength, farStrength, linearDepth);
}

// Motion-aware sharpening modifier for TAA/DLSS recovery
// Static areas get more sharpening, moving areas less
// (since TAA already preserves detail in motion)
float MotionSharpenFactor(float2 motionVector, float motionThreshold, float motionReduction)
{
    float motionMag = length(motionVector);
    float motionFactor = 1.0 - smoothstep(0.0, motionThreshold, motionMag) * motionReduction;
    return saturate(motionFactor);
}

#endif // PROSHARPEN_ADAPTIVE_SHARPEN_INCLUDED
