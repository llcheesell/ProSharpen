# Sharpening Algorithms Research

## Overview

This document covers real-time and post-production sharpening techniques evaluated for ProSharpen.

---

## 1. Contrast Adaptive Sharpening (CAS) — AMD FidelityFX

### Algorithm
- Analyzes local contrast in a 3x3 neighborhood
- Computes sharpening weight proportional to available contrast headroom
- Areas with low contrast get stronger sharpening; already-sharp areas get less
- Uses a negative-lobe cross filter: `result = (w*(b+d+f+h) + e) / (4w + 1)`
- Weight `w` derived from: `contrast = 1 - max + min`, then `w = contrast * peak`
- `peak = -1/lerp(8, 5, sharpness)` controls maximum negative lobe

### Strengths
- Extremely fast (single pass, 5-9 taps)
- Inherently artifact-resistant due to contrast-adaptive weight
- Excellent for TAA blur recovery
- No halo artifacts by design

### Weaknesses
- Limited control (single sharpness parameter)
- Cannot target specific detail scales
- Not suitable for aggressive post-production sharpening

### Performance
~0.1ms at 1080p on modern GPUs. Negligible overhead.

### Reference
- AMD GPUOpen FidelityFX CAS: https://gpuopen.com/fidelityfx-cas/

---

## 2. Robust Contrast Adaptive Sharpening (RCAS) — AMD FSR

### Algorithm
- Evolution of CAS used in FidelityFX Super Resolution (FSR)
- Mathematically solves for the **maximum** sharpening weight before clipping
- Computes per-channel clipping bounds: `w = -m/(n+e+w+s)` and `w = (1-m)/(n+e+w+s-4)`
- Selects the most conservative weight across R/G/B channels
- Built-in **noise detection** using a normalized high-pass response:
  `nz = 0.25*(b+d+f+h) - e`, normalized by local range
- Noise factor reduces sharpening of grain/noise patterns

### Strengths
- Guaranteed artifact-free (mathematically proven bounds)
- Built-in film grain preservation
- Optimal sharpening per pixel
- Same performance as CAS (5-tap cross pattern)

### vs CAS
- More precise weight calculation (solves exact bounds vs heuristic)
- Better noise handling
- Slightly more complex math but same tap count

### Reference
- AMD GPUOpen FSR source code (ffx_fsr1.h)

---

## 3. Unsharp Mask (USM)

### Algorithm
- Classic: `output = original + amount * (original - blurred)`
- `blurred` typically computed via Gaussian blur (3x3 or 5x5)
- Parameters: amount (strength), radius (blur size), threshold (noise gate)

### Enhancements for Real-time
- **Luminance-space processing**: Sharpen only luminance to avoid color shifts
  `new_lum = lum + detail * amount; result = color * (new_lum / lum)`
- **Noise threshold**: `smoothstep(t*0.5, t*1.5, |detail|)` gates sharpening
- **Soft clamping**: `sign(x) * limit * (|x| / (|x| + limit))` prevents halos
- **Edge-aware weighting**: Use bilateral weights to preserve edges

### Strengths
- Familiar to photographers and VFX artists
- Fine-grained control (amount, radius, threshold)
- Supports different blur radii for targeting different detail scales

### Weaknesses
- Can produce halos without proper clamping
- Amplifies noise without threshold
- Slower than CAS for equivalent quality

### Performance
3x3: ~0.15ms, 5x5: ~0.3ms at 1080p

---

## 4. Adaptive Sharpening

### Algorithm
Our custom implementation combining multiple analysis signals:
1. **Local contrast analysis** (Michelson contrast): `(max-min)/(max+min)`
   - Low contrast → sharpen more (detail recovery)
   - High contrast → sharpen less (avoid over-emphasis)
2. **Sobel edge detection**: Gradient magnitude from Sobel-X and Sobel-Y
   - Strong edges → reduce sharpening (prevent jaggies)
3. **Adaptive strength**: `strength = intensity * contrastMod * edgeMod`
4. **Laplacian high-pass**: Combined cross and diagonal for smoother result
5. **Luminance-space application**: Preserves color fidelity

### Strengths
- Context-aware: different behavior for edges, flat areas, textures
- Configurable edge protection and detail boost
- Best quality among all modes
- Resistant to both halos and jaggies

### Weaknesses
- Most computationally expensive
- More parameters to tune
- ~2x cost of CAS

---

## 5. TAA/DLSS Recovery Considerations

### Problem
TAA and upscaling solutions (DLSS, FSR, XeSS) introduce temporal softness:
- Temporal blending smooths high-frequency detail
- Sub-pixel jitter causes ghosting that is counteracted by clamping, which loses detail
- Motion causes additional blur in the temporal history

### Solution: Motion-Aware Sharpening
- Sample motion vectors from the TAA pass
- Static pixels (low motion): apply full sharpening
- Moving pixels (high motion): reduce sharpening to avoid amplifying TAA artifacts
- `factor = 1 - smoothstep(0, threshold, |motionVector|) * reduction`

### Injection Point
- Must run **after** TAA/post-processing to sharpen the final output
- HDRP: `AfterPostProcess` injection point
- URP: `AfterRenderingPostProcessing` event

---

## 6. Depth-Aware Sharpening

### Motivation
- Foreground objects benefit from more sharpening (player focus)
- Background/distant objects can be softer (depth of field interaction)
- Prevents over-sharpening of distant geometry (which has less detail anyway)

### Implementation
- Sample depth buffer
- Compute linear depth factor
- Interpolate between near/far sharpening strengths
- `factor = lerp(nearStrength, farStrength, linearDepth)`

---

## 7. Selective/Layer Masking

### Approaches Evaluated

#### A. Stencil Buffer (HDRP)
- Write stencil values during object rendering
- Read stencil in post-process to mask sharpening
- **Pro**: No extra draw calls, precise per-pixel masking
- **Con**: HDRP-specific, limited stencil bits available

#### B. Custom Pass Mask Rendering (HDRP)
- Use HDRP CustomPass to render target layers into a mask RT
- Reference mask in the sharpen pass
- **Pro**: Full layer control, clean separation
- **Con**: Additional draw calls for mask objects

#### C. Camera Replacement Shader (Universal)
- Separate camera renders target layers with a white shader
- Result used as mask texture
- **Pro**: Works on any pipeline
- **Con**: Full extra scene render (expensive)

#### D. External Mask Texture
- Artist-authored texture
- **Pro**: Full artistic control, zero runtime cost
- **Con**: Not dynamic, requires UV unwrapping

### Selected Approach
- HDRP: CustomPass mask rendering (option B) — best quality/control balance
- URP: Camera replacement (option C) as default, texture mask as lightweight option
- Both: Support for user-provided texture masks

---

## 8. Artifact Prevention Strategies

### Halo Prevention
1. **Neighborhood clamping**: Clamp result to local min/max range
2. **Soft clamping**: `sign(x) * L * (|x|/(|x|+L))` for gradual falloff
3. **Conservative weight selection** (RCAS): Never exceed clipping bounds

### Noise Amplification Prevention
1. **Threshold/noise gate**: Don't sharpen differences below threshold
2. **RCAS noise detection**: High-pass normalized by local range
3. **Luminance-space processing**: Noise is less perceptually visible in chroma

### Jaggy Prevention
1. **Edge detection**: Reduce sharpening on detected edges (Adaptive mode)
2. **Neighborhood clamping with margin**: Allow slight overshoot only
3. **Run after AA**: Never sharpen before anti-aliasing

### Color Shift Prevention
1. **Luminance-only sharpening**: Process in Y (luminance) space
2. **Preserve chrominance**: `result = original * (newLum / oldLum)`

---

## 9. Performance Summary

| Algorithm | Taps | Cost (1080p) | Quality | Artifact Resistance |
|-----------|------|-------------|---------|-------------------|
| CAS       | 5-9  | ~0.1ms      | Good    | Excellent         |
| RCAS      | 5    | ~0.1ms      | Good+   | Perfect           |
| USM 3x3   | 9    | ~0.15ms     | Good    | Good (with clamp) |
| USM 5x5   | 25   | ~0.3ms      | Great   | Good (with clamp) |
| Adaptive  | 9    | ~0.2ms      | Best    | Very Good         |

All algorithms are well within real-time budgets. A separate "rendering mode"
is not necessary as even the most expensive variant takes < 0.5ms.

---

## 10. Recommendations

### Default Mode: RCAS
- Best balance of quality, performance, and artifact resistance
- Built-in noise handling
- Mathematically guaranteed artifact-free

### Professional Use: Adaptive
- When fine control over edge protection and detail boost is needed
- Film/cinematic rendering where quality is paramount

### Quick/Mobile: CAS
- Simplest single-parameter control
- Fastest performance

### Photographic: Unsharp Mask
- Artists familiar with Photoshop/Lightroom workflows
- When specific blur radius targeting is needed

---

## 11. Academic References

1. **FidelityFX CAS** — AMD GPUOpen (2019). Contrast Adaptive Sharpening.
2. **FidelityFX FSR** — AMD GPUOpen (2021). FidelityFX Super Resolution 1.0, RCAS pass.
3. **Unsharp Masking** — Digital Image Processing, Gonzalez & Woods. Classic USM formulation.
4. **Bilateral Filtering** — Tomasi & Manduchi (1998). "Bilateral Filtering for Gray and Color Images." ICCV.
5. **Guided Filter** — He, Sun, Tang (2013). "Guided Image Filtering." IEEE TPAMI.
6. **LumaSharpen** — CeeJay.dk, ReShade shader collection.
7. **AdaptiveSharpen** — Tuukka Korhonen, ReShade shader collection.
8. **ACES Sharpening** — Academy Color Encoding System reference implementation.
