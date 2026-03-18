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

// Noise detection shared by both quality levels
// Returns a suppression factor [0.2, 1.0]: lower = more noise detected
float RCASNoiseDetect(float lumB, float lumD, float lumE, float lumF, float lumH, float noiseThreshold)
{
    float nz = 0.25 * (lumB + lumD + lumF + lumH) - lumE;
    float range = max(max(lumB, lumD), max(lumF, lumH)) - min(min(lumB, lumD), min(lumF, lumH));
    float normalized = saturate(abs(nz) * rcp(range + 0.05));
    // Stronger suppression curve: from 1.0 (no noise) to 0.2 (strong noise)
    float factor = -0.8 * normalized + 1.0;
    // Apply noise threshold: suppress sharpening when high-pass signal is below threshold
    float gate = smoothstep(noiseThreshold * 0.5, noiseThreshold * 2.0, abs(nz));
    return factor * lerp(0.2, 1.0, gate);
}

// RCAS sharpening using 5-tap cross pattern
// b = north, d = west, e = center, f = east, h = south
float3 ApplyRCAS(float3 b, float3 d, float3 e, float3 f, float3 h,
                 float sharpness, float noiseThreshold)
{
    float lumB = Luminance709(b);
    float lumD = Luminance709(d);
    float lumE = Luminance709(e);
    float lumF = Luminance709(f);
    float lumH = Luminance709(h);

    // Noise suppression
    float nz = RCASNoiseDetect(lumB, lumD, lumE, lumF, lumH, noiseThreshold);

    // Compute per-channel optimal weights
    // Solve for the weight that brings the output to the edge of local bounds
    float3 mn4 = min(min(b, d), min(f, h));
    float3 mx4 = max(max(b, d), max(f, h));

    // Safe bounds computation (handles near-1.0 and >1.0 HDR values)
    float3 hitMin = mn4 / (4.0 * mx4 + 1e-4);
    float3 hitMax = max(1.0 - mx4, 0.0) / (4.0 * max(1.0 - mn4, 1e-4) + 1e-4);

    // Take the most conservative weight across channels
    float3 lobeRGB = max(-hitMin, -hitMax);
    float lobe = max(lobeRGB.r, max(lobeRGB.g, lobeRGB.b));

    // Apply user sharpness control
    // Map [0, 1] to [-0.5, 0.0]: 1.0 = strongest
    float peak = lerp(0.0, -0.5, sharpness);
    lobe = max(lobe, peak);

    // Apply noise suppression
    lobe *= nz;

    // Apply the filter
    float3 result = (lobe * (b + d + f + h) + e) / (4.0 * lobe + 1.0);

    return max(result, 0.0);
}

// High quality RCAS with additional corner samples for more robust analysis
float3 ApplyRCASHighQuality(float3 a, float3 b, float3 c,
                            float3 d, float3 e, float3 f,
                            float3 g, float3 h, float3 i,
                            float sharpness, float noiseThreshold)
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

    float nz = RCASNoiseDetect(lumB, lumD, lumE, lumF, lumH, noiseThreshold);

    // Solve for optimal weight using refined bounds (HDR-safe)
    float3 hitMin = mn / (4.0 * mx + 1e-4);
    float3 hitMax = max(1.0 - mx, 0.0) / (4.0 * max(1.0 - mn, 1e-4) + 1e-4);

    float3 lobeRGB = max(-hitMin, -hitMax);
    float lobe = max(lobeRGB.r, max(lobeRGB.g, lobeRGB.b));

    float peak = lerp(0.0, -0.5, sharpness);
    lobe = max(lobe, peak);
    lobe *= nz;

    float3 result = (lobe * (b + d + f + h) + e) / (4.0 * lobe + 1.0);

    return max(result, 0.0);
}

#endif // PROSHARPEN_RCAS_INCLUDED
