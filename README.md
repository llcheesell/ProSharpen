# ProSharpen

Professional-grade sharpening post-process for Unity HDRP and URP.

## Features

- **4 Sharpening Algorithms** with distinct characteristics (see below)
- **Realtime / High Quality** modes per algorithm
- **TAA/DLSS Recovery** — Motion-aware sharpening for temporal AA softness recovery (HDRP)
- **Depth-Aware** — Modulate sharpening by distance
- **Layer Masking** — Selective sharpening via mask textures or layer-based passes
- **Artifact Prevention** — Neighborhood clamping, soft clamping, noise gating

## Requirements

- Unity 2022.3+ (LTS) or Unity 6
- HDRP 14+ or URP 14+

## Installation

### Via Git URL (Package Manager)

1. Open **Window > Package Manager**
2. Click **+** > **Add package from git URL**
3. Enter the repository URL

### Via Git Submodule

```bash
git submodule add <repository-url> Packages/ProSharpen
```

## Quick Start

### HDRP

1. **Edit > Project Settings > HDRP** > Add `ProSharpenVolume` to **After Post Process** list
2. Add a **Volume** to your scene
3. Add **ProSharpen** override, enable and adjust

### URP

1. Select your **URP Renderer Data** asset
2. Add **ProSharpen Renderer Feature**
3. Add a **Volume** to your scene
4. Add **ProSharpen** override, enable and adjust

---

## Algorithm Guide

### CAS (Contrast Adaptive Sharpening)

AMD FidelityFX CAS ベース。ローカルコントラストを分析し、クリッピングせずにシャープにできる量を自動計算。

**仕組み:** 周辺ピクセルの min/max からコントラストの余裕を算出し、フィルタの negative lobe weight を決定。余裕が大きい場所ほど強くシャープにかかる。

| | Realtime | High |
|---|---|---|
| サンプル | 十字 5 タップ | 全 9 タップ (3x3) + Corner Weight |
| min/max 推定 | 十字のみ | 3x3 全域 |
| パラメータ | Sharpness | Sharpness, Corner Weight |

- **長所:** 極めて高速、リンギング/ハローが出にくい、汎用性が高い
- **短所:** ノイズ抑制なし（粒状感が残りうる）
- **推奨用途:** リアルタイム描画、ゲーム、一般的なシーン

### RCAS (Robust CAS)

AMD FidelityFX FSR の RCAS パスベース。CAS の数学的改良版。

**仕組み:** クリッピングが起きるギリギリのフィルタウェイトをチャンネルごとに厳密に逆算。ノイズ検出（高周波成分の正規化 + ノイズゲート）で粒状感の増幅を抑制。

| | Realtime | High |
|---|---|---|
| サンプル | 十字 5 タップ | 十字 5 タップ (フィルタ) + コーナー 4 タップ (bounds) |
| min/max 推定 | 4 タップ | 4 タップ + 8 タップの平均 |
| ノイズ抑制 | あり (強化済み) | あり |

- **長所:** CAS より数学的に最適、組み込みノイズ耐性、FSR パイプラインとの親和性
- **短所:** CAS より若干重い
- **推奨用途:** FSR/DLSS 後のリカバリ、ノイズの多い入力

### Unsharp Mask (USM)

写真・画像編集の古典的手法。

**仕組み:** `出力 = 元画像 + amount * (元画像 - ぼかし画像)` 。ぼかしとの差分（ディテール信号）を増幅して加算。Noise Threshold で微小な差分（ノイズ）をゲートし、Clamp Limit で過剰な補正を防止。

| | Realtime | High |
|---|---|---|
| ブラーカーネル | 3x3 Gaussian (sigma ~0.85) | 3x3 Box (sigma ~1.5、より広い) |
| ディテール信号 | 繊細・局所的 | 大きい・明瞭 |

- **長所:** 直感的パラメータ、Luma Only で色ずれ防止、細かい制御が可能
- **短所:** ハロー/リンギングが出やすい（SoftClamp で軽減）
- **推奨用途:** 写真的な仕上げ、明確なディテール強調

> **Note:** Sharpness スライダー [0, 1] は内部で [0, 2.5] にリマップされます。

### Adaptive Sharpen

複合アプローチの独自実装。

**仕組み:** Sobel エッジ検出 + Michelson コントラスト分析でピクセルごとにシャープ強度を自動調整。低コントラスト域は強化、エッジ付近は抑制。

- Edge Protection: エッジ上のジャギー防止度合い
- Detail Boost: 低コントラスト域の追加強化量

| | Realtime | High |
|---|---|---|
| 差異 | なし | なし |

- **長所:** 最もインテリジェントな適応処理、手動調整が少なくて済む
- **短所:** 4 つの中で最も重い
- **推奨用途:** 最終出力品質重視、プリレンダ

---

## Parameters

### Core

| パラメータ | 範囲 | 説明 |
|---|---|---|
| **Mode** | Enum | シャープニングアルゴリズム選択 |
| **Quality** | Realtime / High | 品質レベル |
| **Intensity** | 0 - 1 | 元画像とシャープ結果のブレンド比 |
| **Sharpness** | 0 - 1 | シャープニング強度 |

### Advanced (モード別)

| パラメータ | 表示モード | 説明 |
|---|---|---|
| **Luminance Only** | 全モード | 輝度空間でのみ処理（色ずれ防止） |
| **Edge Protection** | Adaptive | エッジ上のシャープ抑制量 |
| **Detail Boost** | Adaptive | 低コントラスト域の追加強化 |
| **Noise Threshold** | USM, RCAS | この値以下の差分はシャープしない |
| **Clamp Limit** | USM | 最大補正量（ハロー防止） |
| **Corner Weight** | CAS (High) | コーナーサンプルの重み |

### Depth-Aware

| パラメータ | 説明 |
|---|---|
| **Near Strength** | 近距離のシャープニング倍率 |
| **Far Strength** | 遠距離のシャープニング倍率 |
| **Near / Far Plane** | 補間範囲 |

### TAA/DLSS Recovery (HDRP only)

| パラメータ | 説明 |
|---|---|
| **Motion Threshold** | この速度以上の領域でシャープを減衰 |
| **Motion Reduction** | 動いている領域のシャープ減衰量 |

---

## Selective Sharpening

### Mask Texture
グレースケールテクスチャを割り当て。白 = フルシャープ、黒 = シャープなし。

### Layer-Based (HDRP)
`ProSharpenStencilMaskPass` Custom Pass で特定レイヤーからマスクを自動生成。

### Layer-Based (Camera)
`ProSharpenMaskGenerator` をカメラにアタッチし、生成される `MaskTexture` を Volume に割り当て。

---

## Algorithm Selection Guide

| 用途 | 推奨 |
|---|---|
| ゲーム・リアルタイム (高速) | **CAS** Realtime |
| FSR / DLSS 後のリカバリ | **RCAS** |
| 写真的・精密な仕上げ | **USM** High |
| 自動・手軽な高品質 | **Adaptive** |
| ノイズの多いソース | **RCAS** (Noise Threshold 調整) |

## License

MIT License. See [LICENSE](LICENSE) for details.
