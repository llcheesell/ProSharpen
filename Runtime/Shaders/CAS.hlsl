#ifndef PROSHARPEN_CAS_INCLUDED
#define PROSHARPEN_CAS_INCLUDED

// ============================================================================
// Contrast Adaptive Sharpening (CAS)
// Based on AMD FidelityFX CAS algorithm
// Reference: https://gpuopen.com/fidelityfx-cas/
//
// CAS works by analyzing local contrast and applying sharpening proportional
// to how much room there is to sharpen without exceeding the local min/max.
// This makes it inherently artifact-resistant.
// ============================================================================

#include "ProSharpenCommon.hlsl"

// CAS sharpening on a 3x3 neighborhood
// Input: 3x3 neighborhood samples (a-i), sharpness factor
// Output: sharpened center pixel color
//
//  a b c
//  d e f    e = center pixel
//  g h i
//
float3 ApplyCAS(float3 a, float3 b, float3 c,
                float3 d, float3 e, float3 f,
                float3 g, float3 h, float3 i,
                float sharpness)
{
    // Convert to luminance for contrast analysis
    float lumA = Luminance709(a);
    float lumB = Luminance709(b);
    float lumC = Luminance709(c);
    float lumD = Luminance709(d);
    float lumE = Luminance709(e);
    float lumF = Luminance709(f);
    float lumG = Luminance709(g);
    float lumH = Luminance709(h);
    float lumI = Luminance709(i);

    // Find local min and max in the cross neighborhood (b, d, e, f, h)
    float minLum = min(lumE, min(min(lumB, lumD), min(lumF, lumH)));
    float maxLum = max(lumE, max(max(lumB, lumD), max(lumF, lumH)));

    // Also consider corners for a more robust estimate
    float minLumFull = min(minLum, min(min(lumA, lumC), min(lumG, lumI)));
    float maxLumFull = max(maxLum, max(max(lumA, lumC), max(lumG, lumI)));

    // Compute the sharpening amount based on local contrast
    // The key insight: when there's a lot of contrast headroom,
    // we can sharpen more without clipping
    float contrast = 1.0 - maxLum + minLum;

    // Compute the CAS filter weight
    // sharpness: 0.0 = no sharpening, 1.0 = maximum
    // The negative lobe weight is derived from contrast headroom
    float peak = -rcp(lerp(8.0, 5.0, saturate(sharpness)));
    float w = contrast * peak;

    // Apply the filter: negative lobe on cross neighbors, positive on center
    // This is essentially an edge-preserving high-pass filter
    float3 result = (w * (b + d + f + h) + e) / (4.0 * w + 1.0);

    // Clamp to local neighborhood to prevent ringing
    float3 minColor = min(e, min(min(b, d), min(f, h)));
    float3 maxColor = max(e, max(max(b, d), max(f, h)));
    result = clamp(result, minColor, maxColor);

    return result;
}

// Extended CAS with corner samples for higher quality
float3 ApplyCASHighQuality(float3 a, float3 b, float3 c,
                           float3 d, float3 e, float3 f,
                           float3 g, float3 h, float3 i,
                           float sharpness, float cornerWeight)
{
    float lumA = Luminance709(a);
    float lumB = Luminance709(b);
    float lumC = Luminance709(c);
    float lumD = Luminance709(d);
    float lumE = Luminance709(e);
    float lumF = Luminance709(f);
    float lumG = Luminance709(g);
    float lumH = Luminance709(h);
    float lumI = Luminance709(i);

    // Full 3x3 neighborhood min/max
    float minLum = min(lumE, min(min(min(lumA, lumB), min(lumC, lumD)),
                                min(min(lumF, lumG), min(lumH, lumI))));
    float maxLum = max(lumE, max(max(max(lumA, lumB), max(lumC, lumD)),
                                max(max(lumF, lumG), max(lumH, lumI))));

    float contrast = 1.0 - maxLum + minLum;
    float peak = -rcp(lerp(8.0, 5.0, saturate(sharpness)));
    float w = contrast * peak;
    float wCorner = w * cornerWeight;

    // Apply with corner contributions
    float3 result = (w * (b + d + f + h) + wCorner * (a + c + g + i) + e)
                  / (4.0 * w + 4.0 * wCorner + 1.0);

    // Neighborhood clamping
    float3 minColor = min(e, min(min(min(a, b), min(c, d)), min(min(f, g), min(h, i))));
    float3 maxColor = max(e, max(max(max(a, b), max(c, d)), max(max(f, g), max(h, i))));
    result = clamp(result, minColor, maxColor);

    return result;
}

#endif // PROSHARPEN_CAS_INCLUDED
