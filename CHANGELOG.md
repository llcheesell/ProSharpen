# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Halo Control: 明るい側 (Overshoot) と暗い側 (Undershoot) を別々に制限する非対称ソフトリミッタを全モードに追加
- Unsharp Mask に Radius を追加（Realtime: 5x5 ガウス相当 / High: 9x9 ガウス相当）
- Adaptive に方向性シャープを追加（強いエッジではエッジを横切る方向だけをシャープ）
- HDRP: `ProSharpenStencilMaskPass` に深度テストを追加。生成したマスクが Volume から自動で使われるように
- `ProSharpenCore.hlsl`（パイプライン共通のシャープ処理本体）

### Changed
- CAS / RCAS を AMD の式に準拠する形へ書き直し（CAS の適応量、RCAS の lobe 下限とノイズ抑制）
- USM / Adaptive を知覚空間（ガンマ 2.0）での処理に変更。ハロー制限と合成は全モードで知覚空間
- Luminance Only が全モードで有効に（従来は USM のみ）
- Noise Threshold を局所コントラストによるゲートに統一し、Adaptive でも使用
- Clamp Limit を全モード共通の「Detail Limit」（1 画素あたりの最大変化量）に変更
- 深度・モーション・マスクはシャープ量への乗算に変更（従来は結果のブレンドを 3 回実行）
- ソースのアルファを保持して出力
- シェーダーキーワードを `multi_compile_local_fragment` に変更し、バリアント数を削減

### Fixed
- Unity 6.3 で変更された `CoreUtils.DrawRendererList` を使わず `CommandBuffer.DrawRendererList` を呼ぶように（Unity 2022.3〜6.3 でコンパイル可能）
- CAS: Sharpness が高いとき、平坦部でフィルタの分母が 0 になる問題
- CAS High: Corner Weight により分母が負になる問題
- RCAS: lobe が -0.25 に達して分母が 0 になる問題、Noise Threshold = 0 での不定値
- HDRP: 動的解像度 / アップスケーラ使用時に、深度とモーションベクターの参照座標がずれる問題
- HDRP: 画面端でビューポート外のテクセルを参照しうる問題
- HDRP: マスク用シェーダーをカメラ相対レンダリングに対応
- HDRP プロジェクトで URP 用 SubShader のインクルードエラーが出る問題（`PackageRequirements` を追加）
- HDRP: マスクをカメラごとに保持・受け渡すように（サイズの違うカメラが同時に描画されるとマスクが外れる問題を回避）
- URP: Render Graph で MSAA のカメラカラーを処理する場合は差し替えずコピーし戻すように
- URP: 未対応の TAA/DLSS Recovery をインスペクターから非表示に
- URP: Unity 6.3 (URP 17.3) でコンパイルエラーになる問題、`_BlitTexture_TexelSize` の再定義、Render Graph で深度入力が要求されない問題

## [0.1.0] - 2026-03-17

### Added
- Initial release
- Contrast Adaptive Sharpening (CAS) based on AMD FidelityFX
- Unsharp Mask with edge-aware clamping
- Adaptive Sharpening with local contrast analysis
- HDRP Custom Post Process support (Unity 2022.3+, HDRP 14+)
- URP Renderer Feature support (Unity 2022.3+, URP 14+)
- Unity 6 compatibility
- Layer-based selective sharpening via stencil masking
- Real-time and high-quality rendering modes
- TAA/DLSS softness recovery mode
