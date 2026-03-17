#ifndef PROSHARPEN_COMMON_INCLUDED
#define PROSHARPEN_COMMON_INCLUDED

// ============================================================================
// ProSharpen Common - Shared utilities for all sharpening algorithms
// ============================================================================

// Sharpening mode constants
#define SHARPEN_MODE_CAS          0
#define SHARPEN_MODE_UNSHARP_MASK 1
#define SHARPEN_MODE_ADAPTIVE     2

// Quality level constants
#define QUALITY_REALTIME   0
#define QUALITY_HIGH       1

// Perceptual luminance weights (BT.709)
static const float3 LUMA_BT709 = float3(0.2126, 0.7152, 0.0722);

// Convert RGB to perceptual luminance
float Luminance709(float3 color)
{
    return dot(color, LUMA_BT709);
}

// Soft clamping to prevent ringing/halo artifacts
// Maps values smoothly to [-limit, limit] range
float SoftClamp(float value, float limit)
{
    float absVal = abs(value);
    float sign = value >= 0.0 ? 1.0 : -1.0;
    return sign * limit * (absVal / (absVal + limit));
}

float3 SoftClamp3(float3 value, float limit)
{
    return float3(
        SoftClamp(value.x, limit),
        SoftClamp(value.y, limit),
        SoftClamp(value.z, limit)
    );
}

// Edge-aware weight based on luminance difference
// Returns lower weight for large differences (edges)
float EdgeWeight(float lumCenter, float lumNeighbor, float edgeThreshold)
{
    float diff = abs(lumCenter - lumNeighbor);
    return exp(-diff * diff / (edgeThreshold * edgeThreshold + 1e-6));
}

// Remap value from [0,1] user range to algorithm-specific range
float RemapIntensity(float userIntensity, float minVal, float maxVal)
{
    return lerp(minVal, maxVal, userIntensity);
}

// Prevent sharpening from pushing values below 0 or above max
float3 SafeSharpen(float3 original, float3 sharpened, float maxOverbright)
{
    float3 minVal = original * (1.0 - maxOverbright);
    float3 maxVal = original * (1.0 + maxOverbright) + maxOverbright;
    return clamp(sharpened, max(minVal, 0.0), maxVal);
}

#endif // PROSHARPEN_COMMON_INCLUDED
