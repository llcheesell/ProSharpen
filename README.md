# ProSharpen

Professional-grade sharpening post-process for Unity HDRP and URP.

## Features

- **4 Sharpening Algorithms** with distinct characteristics (see below)
- **Realtime / High Quality** modes per algorithm
- **TAA/DLSS Recovery** — Motion-aware sharpening for temporal AA softness recovery (HDRP)
- **Depth-Aware** — Modulate sharpening by distance
- **Layer Masking** — Selective sharpening via mask textures or layer-based passes
- **Halo Control** — 明るい側 / 暗い側を別々に制限する非対称ソフトリミッタ、ノイズゲート
- **Perceptual Processing** — 知覚空間でのハロー制御と合成、輝度のみ / RGB の切り替え

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

## 処理の流れ

すべてのモードは次の共通パイプラインを通ります。

1. 各モードが近傍画素からシャープ量を求める（CAS / RCAS は AMD の仕様どおりリニア信号、USM / Adaptive は知覚空間＝ガンマ 2.0）
2. シャープ量を知覚空間の値に揃える
3. **Noise Threshold**: 局所コントラストが小さい平坦部はシャープしない（全モード共通。0 で無効）
4. Intensity・深度・モーション・マスクを掛ける
5. **Halo Control**: 出力が「近傍の最小値 − Undershoot 〜 近傍の最大値 + Overshoot」に収まるよう滑らかに制限し、さらに **Detail Limit** で 1 画素あたりの変化量に上限を掛ける
6. 知覚空間で中心画素へ加算し、リニアへ戻す（アルファはそのまま通す）

**Luminance Only** は全モードで有効です。オンにすると輝度だけでシャープ量を求めて RGB に同じ量を加えるため、色のにじみが出ません。オフにすると RGB 各チャンネルを個別に処理します。

> HDRP の TAA シャープ（Sharpen Strength / History Sharpening）や FSR の Sharpness と併用すると二重にシャープがかかります。ProSharpen を使う場合はそちらを弱めてください。

### アンチエイリアス / アップスケーラとの併用

実描画での検証結果は [Documents/Verification_2026-11.md](Documents/Verification_2026-11.md) にあります。要点は次のとおりです。

| 併用する機能 | 相性 | 補足 |
|---|---|---|
| SMAA | 良好 | AA なしと同じ効き方 |
| TAA | 良好 | TAA 側の Sharpen Strength は 0 を推奨（0.5 だと ProSharpen の前段でちらつきが 15〜49% 増える） |
| DLSS / DLAA | 良好 | アップスケール後の解像度で処理する。動的解像度でも位置ずれなし |
| FXAA | 効果が弱まる | HDRP は FXAA を ProSharpen の後で実行するため、シャープが 5〜7 割に弱まる |
| フィルムグレイン | 影響なし | HDRP ではグレインが ProSharpen の後で加わるため増幅されない |

シャープは時間方向のちらつきも同じ割合で強めます。TAA / DLSS のちらつきが気になる場合は **RCAS**（増幅が最も小さい）を選ぶか、Noise Threshold を上げてください。

---

## Algorithm Guide

### CAS (Contrast Adaptive Sharpening)

AMD FidelityFX CAS（`ffx_cas.h`）の式に準拠。近傍の min/max から「クリップせずにシャープできる余裕」を求め、その平方根をフィルタの負ローブに掛けます。

| | Realtime | High |
|---|---|---|
| サンプル | 十字 5 タップ | 3x3 の 9 タップ |
| min/max 推定 | 十字 | 十字 + 対角（AMD の "better diagonals"） |
| パラメータ | Sharpness | Sharpness, Corner Weight |

- **長所:** 最も軽い。コントラストの高い場所ほど自動で弱くなる
- **短所:** 低コントラストな部分ほど強く掛かるため、Noise Threshold を 0 にすると TAA / DLSS の微小なちらつきを平坦部で増幅する
- **推奨用途:** 軽さ優先の常用シャープ

Corner Weight は負ローブのうち対角へ配分する割合です（0 で十字のみ）。合計のシャープ量は変わりません。

### RCAS (Robust CAS)

AMD FidelityFX FSR1 の RCAS（`ffx_fsr1.h`）に準拠。出力が 0〜1 をはみ出さない限界のフィルタ重みをチャンネルごとに解き、最も控えめな値を使います。孤立したノイズ画素では重みを最大 50% 下げます。

| | Realtime | High |
|---|---|---|
| サンプル | 十字 5 タップ | 十字 5 タップ（フィルタ）+ 対角 4 タップ（min/max） |
| min/max 推定 | 4 タップ | 4 タップと 8 タップの平均（斜めエッジで控えめ） |

- **長所:** ノイズ耐性がある。アップスケーラ後のリカバリに向く
- **推奨用途:** FSR / DLSS / TAA 後のリカバリ、ノイズのある入力

### Unsharp Mask (USM)

`出力 = 元画像 + amount * (元画像 − ガウスぼかし)`。**Radius**（ガウスの標準偏差、ピクセル）で強調するディテールの太さを選べます。

| | Realtime | High |
|---|---|---|
| サンプル | 3x3 = 9 バイリニアタップ | 5x5 = 25 バイリニアタップ |
| ぼかし | 5x5 ガウス相当（Radius 約 1.3 まで） | 9x9 ガウス相当（Radius 約 2.5 まで） |

バイリニア補間を使って 2 テクセルを 1 タップで読むため、タップ数の割に正確なガウスになります。

- **長所:** 半径を選べる。写真的な仕上がり
- **短所:** 半径が大きいほどハローが出やすい（Halo Control で制限）
- **推奨用途:** 明確なディテール強調、やや太い質感の強調

> **Note:** Sharpness スライダー [0, 1] は内部で Amount [0, 2.5] にリマップされます。

### Adaptive Sharpen

3x3 近傍を解析し、画素ごとにシャープの掛け方を変える独自実装です。

- **Detail Boost:** 低〜中コントラストの質感（肌、布、髪など）のゲインを上げる
- **Edge Protection:** 強いエッジ（輪郭線、LED の画素境界など）のゲインを下げ、さらにシャープの向きを「エッジを横切る方向」だけに絞る

等方的なシャープはエッジに沿った方向の凹凸（ジャギーの階段）も増幅します。Edge Protection を上げると、勾配方向の 2 階微分だけを使う方向性シャープへ切り替わり、エッジの立ち上がりは鋭くしつつ階段は増幅しにくくなります。

| | Realtime | High |
|---|---|---|
| 差異 | なし（常に 9 タップ） | なし |

- **推奨用途:** トゥーン調や高コントラストなエッジが多い画面で、質感だけを上げたい場合

---

## Parameters

### Core

| パラメータ | 範囲 | 説明 |
|---|---|---|
| **Mode** | Enum | シャープニングアルゴリズム選択 |
| **Quality** | Realtime / High | 品質レベル |
| **Intensity** | 0 - 1 | シャープ量全体の倍率 |
| **Sharpness** | 0 - 1 | アルゴリズムのシャープ強度 |

### Advanced (モード別)

| パラメータ | 表示モード | 説明 |
|---|---|---|
| **Luminance Only** | 全モード | 輝度だけでシャープ量を求める（色にじみ防止） |
| **Edge Protection** | Adaptive | 強いエッジのゲイン低減と方向性シャープへの切り替え量 |
| **Detail Boost** | Adaptive | 低〜中コントラスト域の追加ゲイン |
| **Noise Threshold** | 全モード | 局所コントラストがこの値未満の平坦部はシャープしない。0 で無効（CAS は AMD の実装と同じ挙動になる） |
| **Detail Limit** | 全モード | 1 画素あたりの最大変化量（知覚空間） |
| **Corner Weight** | CAS (High) | 対角サンプルへの負ローブ配分 |
| **Radius** | USM | ぼかし半径（ピクセル） |

### Halo Control

| パラメータ | 既定値 | 説明 |
|---|---|---|
| **Overshoot (Bright)** | 0.01 | 明るい側のハロー許容量。0 で白ハローなし |
| **Undershoot (Dark)** | 0.03 | 暗い側のハロー許容量。0 で黒ハローなし |

値は知覚空間（ガンマ 2.0）での量です。白いハローは黒いハローより目立つため、既定では明るい側を厳しくしています。両方を上げるとエッジのメリハリ（アキュータンス）が強くなり、0 にすると近傍の明るさの範囲を一切超えません。

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

**Selective Masking** を有効にすると、マスクが白い場所だけシャープします。

### Mask Texture
グレースケールテクスチャを割り当て。白 = フルシャープ、黒 = シャープなし。

### Layer-Based (HDRP)
Custom Pass Volume に `ProSharpenStencilMaskPass` を追加すると、指定レイヤーのオブジェクトからマスクを自動生成します。Volume 側の Mask Texture が未指定のとき、このマスクが使われます。

- **Depth Test**（既定オン）: カメラ深度と比較し、手前の物体に隠れた部分をマスクから除外します。深度プリパスより前の Injection Point で使う場合はオフにしてください。
- 頂点アニメーションをシェーダー内で行うマテリアルは、マスクの形状に反映されません。

### Layer-Based (Camera)
`ProSharpenMaskGenerator` は Replacement Shader（`Camera.RenderWithShader`）を使うため、**Built-in Render Pipeline 専用**です。HDRP / URP では正しいマスクになりません。HDRP では上記の Custom Pass を使ってください。

---

## Algorithm Selection Guide

| 用途 | 推奨 |
|---|---|
| 軽さ優先の常用シャープ | **CAS** Realtime |
| FSR / DLSS / TAA 後のリカバリ（ちらつきを増やしたくない） | **RCAS** |
| 半径を選んだ写真的な仕上げ | **USM**（Radius 1.0 前後、太い質感は High + Radius 1.5〜2.5） |
| トゥーン調・高コントラストなエッジが多い画面 | **Adaptive**（Edge Protection 0.5〜1.0） |
| ノイズの多いソース | **RCAS** または **Adaptive**（Noise Threshold 調整） |

## Performance

3840x2160 のフルスクリーンパス 1 回あたりの GPU 時間（RTX 4090、単純なコピーは 0.023 ms）。負荷はほぼタップ数で決まります。

| モード | タップ数 | Luminance Only オン | オフ (RGB) |
|---|---|---|---|
| CAS / RCAS Realtime | 5 | 0.04 ms | 0.06〜0.07 ms |
| CAS / RCAS High、USM Realtime、Adaptive | 9 | 0.06〜0.07 ms | 0.07〜0.10 ms |
| USM High | 25 | 0.16 ms | 0.17 ms |

- Luminance Only はオン（既定）のほうが軽い
- USM High は Radius が 1.3 を超えるときだけ使う。Radius 1.0 では Realtime とほぼ同じ結果で、コストは 2.5 倍
- マスクや深度連動でシャープ量が 0 になる画素は、近傍を読まずにコピーと同じコストで抜ける

## Compatibility

| | 対応状況 |
|---|---|
| HDRP 14 (Unity 2022.3) 〜 HDRP 17 (Unity 6) | 対応 |
| URP 14 (Unity 2022.3) | Compatibility Mode（`Execute`）で動作 |
| URP 17.0〜17.2 (Unity 6.0〜6.2) | Render Graph / Compatibility Mode |
| URP 17.3 以降 (Unity 6.3〜) | Render Graph。Compatibility Mode 用のコードは `URP_COMPATIBILITY_MODE` 定義時のみコンパイル |

URP の Render Graph では、アクティブなカラーターゲットがバックバッファの場合シャープをスキップします（パスは中間テクスチャを要求するため、通常は発生しません）。

## License

MIT License. See [LICENSE](LICENSE) for details.
