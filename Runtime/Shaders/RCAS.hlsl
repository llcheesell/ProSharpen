#ifndef PROSHARPEN_RCAS_INCLUDED
#define PROSHARPEN_RCAS_INCLUDED

// ============================================================================
// Robust Contrast Adaptive Sharpening (RCAS)
// Based on AMD FidelityFX FSR RCAS algorithm
// Reference: AMD GPUOpen FSR source
//
// RCAS improves upon CAS by mathematically solving for the maximum local
// sharpness possible before clipping occurs. This guarantees artifact-free
// results while maximizing detail recovery.
//
// Uses a 5-tap cross pattern filter: output = (w*(n+e+w+s)+m)/(4*w+1)
// where w is adaptively computed per-pixel.
// ============================================================================

#include "ProSharpenCommon.hlsl"

// RCAS sharpening using 5-tap cross pattern
// b = north, d = west, e = center, f = east, h = south
float3 ApplyRCAS(float3 b, float3 d, float3 e, float3 f, float3 h, float sharpness)
{
    // Luma of each tap
    float lumB = Luminance709(b);
    float lumD = Luminance709(d);
    float lumE = Luminance709(e);
    float lumF = Luminance709(f);
    float lumH = Luminance709(h);

    // Min and max of cross neighborhood
    float nz = 0.25 * (lumB + lumD + lumF + lumH) - lumE;
    // Detect potential noise: normalize the high-pass response
    nz = saturate(abs(nz) * rcp(max(max(lumB, lumD), max(lumF, lumH)) -
                                 min(min(lumB, lumD), min(lumF, lumH)) + 0.05));
    // Apply noise suppression curve
    nz = -0.5 * nz + 1.0;

    // Compute per-channel optimal weights
    // Solve for the weight that brings the output to the edge of [0,1]
    // Lower bound: w = -m / (n+e+w+s)
    // Upper bound: w = (1-m) / (n+e+w+s - 4)
    float3 mn4 = min(min(b, d), min(f, h));
    float3 mx4 = max(max(b, d), max(f, h));

    // Clamp to neighborhood range for stability
    float3 hitMin = mn4 / (4.0 * mx4 + 1e-6);
    float3 hitMax = (1.0 - mx4) / (4.0 * (1.0 - mn4) + 1e-6);

    // Take the most conservative weight across channels
    float3 lobeRGB = max(-hitMin, -hitMax);
    float lobe = max(lobeRGB.r, max(lobeRGB.g, lobeRGB.b));

    // Apply user sharpness control
    // sharpness 0.0 = no sharpening, 1.0 = maximum
    // Map to a usable range: -0.5 (strong sharpen) to 0.0 (none)
    float peak = lerp(0.0, -0.5, sharpness);
    lobe = max(lobe, peak);

    // Apply noise factor to reduce sharpening of grain
    lobe *= nz;

    // Apply the filter
    float3 result = (lobe * (b + d + f + h) + e) / (4.0 * lobe + 1.0);

    return result;
}

// High quality RCAS with additional corner samples for more robust analysis
float3 ApplyRCASHighQuality(float3 a, float3 b, float3 c,
                            float3 d, float3 e, float3 f,
                            float3 g, float3 h, float3 i,
                            float sharpness)
{
    // Use corners for better min/max estimation
    float3 mn4 = min(min(b, d), min(f, h));
    float3 mx4 = max(max(b, d), max(f, h));
    float3 mn8 = min(mn4, min(min(a, c), min(g, i)));
    float3 mx8 = max(mx4, max(max(a, c), max(g, i)));

    // Average the 4-tap and 8-tap bounds for a more robust estimate
    float3 mn = (mn4 + mn8) * 0.5;
    float3 mx = (mx4 + mx8) * 0.5;

    // Noise detection
    float lumB = Luminance709(b);
    float lumD = Luminance709(d);
    float lumE = Luminance709(e);
    float lumF = Luminance709(f);
    float lumH = Luminance709(h);

    float nz = 0.25 * (lumB + lumD + lumF + lumH) - lumE;
    nz = saturate(abs(nz) * rcp(max(max(lumB, lumD), max(lumF, lumH)) -
                                 min(min(lumB, lumD), min(lumF, lumH)) + 0.05));
    nz = -0.5 * nz + 1.0;

    // Solve for optimal weight using refined bounds
    float3 hitMin = mn / (4.0 * mx + 1e-6);
    float3 hitMax = (1.0 - mx) / (4.0 * (1.0 - mn) + 1e-6);

    float3 lobeRGB = max(-hitMin, -hitMax);
    float lobe = max(lobeRGB.r, max(lobeRGB.g, lobeRGB.b));

    float peak = lerp(0.0, -0.5, sharpness);
    lobe = max(lobe, peak);
    lobe *= nz;

    float3 result = (lobe * (b + d + f + h) + e) / (4.0 * lobe + 1.0);

    return result;
}

#endif // PROSHARPEN_RCAS_INCLUDED
