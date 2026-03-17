# ProSharpen

Professional-grade sharpening post-process for Unity HDRP and URP.

## Features

- **Multiple Algorithms**
  - **CAS (Contrast Adaptive Sharpening)** — Based on AMD FidelityFX. Fast, artifact-resistant. Best for real-time use.
  - **Unsharp Mask** — Classic photographic sharpening with noise threshold and halo prevention.
  - **Adaptive Sharpening** — Context-aware with edge protection and detail boost. Highest quality.

- **TAA/DLSS Recovery** — Motion-aware mode that sharpens static areas more to counteract temporal AA softness.
- **Depth-Aware** — Modulate sharpening by distance (e.g., sharpen foreground, soften background).
- **Layer Masking** — Apply sharpening selectively using layer-based masks or custom mask textures.
- **Artifact Prevention** — Soft clamping, neighborhood clamping, luminance-space processing, and noise gating.
- **Real-time & High Quality modes** — Choose between optimized 3x3 and extended sampling.

## Requirements

- Unity 2022.3+ (LTS) or Unity 6
- HDRP 14+ or URP 14+

## Installation

### Via Git URL (Package Manager)

1. Open **Window > Package Manager**
2. Click **+** > **Add package from git URL**
3. Enter: `https://github.com/your-repo/ProSharpen.git`

### Via Git Submodule

```bash
git submodule add https://github.com/your-repo/ProSharpen.git Packages/com.llcheesell.prosharpen
```

## Quick Start

### HDRP

1. Open your **HDRP Global Settings** (Edit > Project Settings > HDRP)
2. Add `ProSharpenVolume` to the **After Post Process** custom post-process list
3. Add a **Volume** component to your scene
4. Add **ProSharpen** override to the Volume profile
5. Enable and adjust parameters

### URP

1. Select your **URP Renderer Data** asset
2. Add **ProSharpen Renderer Feature**
3. Add a **Volume** component to your scene
4. Add **ProSharpen** override to the Volume profile
5. Enable and adjust parameters

## Selective Sharpening

### Mask Texture
Assign a grayscale texture where white = full sharpening, black = no sharpening.

### Layer-Based (HDRP)
Use the `ProSharpenStencilMaskPass` Custom Pass to automatically generate a mask from specific layers.

### Layer-Based (Camera)
Attach `ProSharpenMaskGenerator` to your camera, configure the sharpen layers, and assign the generated `MaskTexture` to the Volume component.

## License

MIT License. See [LICENSE](LICENSE) for details.
