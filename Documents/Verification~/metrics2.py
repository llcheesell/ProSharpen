import sys, os, numpy as np
from PIL import Image
from scipy.ndimage import maximum_filter, minimum_filter
cap = sys.argv[1]
W = np.array([0.2126, 0.7152, 0.0722])
def dec(v): v = v / 255.0; return np.where(v <= 0.04045, v / 12.92, ((v + 0.055) / 1.055) ** 2.4)
def load(p):
    a = np.asarray(Image.open(p).convert('RGB'), dtype=np.float64)
    return np.sqrt(dec(a) @ W)          # 知覚空間 (ガンマ 2.0) の輝度 = シェーダーのリミッタと同じ空間
def grad(l): return np.abs(np.diff(l, axis=1)).mean() + np.abs(np.diff(l, axis=0)).mean()
def row(label, off, o):
    mx = maximum_filter(off, 5); mn = minimum_filter(off, 5)   # USM の参照範囲 (±2px) に合わせる
    over = np.maximum(o - mx, 0); under = np.maximum(mn - o, 0)
    print(f'{label:34s} gain={grad(o)/grad(off):.3f} over99.9={np.percentile(over,99.9):.4f} max={over.max():.3f} under99.9={np.percentile(under,99.9):.4f} max={under.max():.3f}')
names = {'cas': 'CAS', 'rcas': 'RCAS', 'usm': 'UNSHARP', 'adaptive': 'ADAPTIVE'}
print('# HDRP 実描画 (AA なし・決定的)')
for tag in ['new', 'old']:
    for scene in ['photo', 'anime', 'stage']:
        off = load(f'{cap}/{tag}_{scene}_none_off.png')
        for sh in ['cas', 'rcas', 'usm', 'adaptive', 'cas_s1', 'rcas_s1', 'usm_s1', 'adaptive_s1']:
            p = f'{cap}/{tag}_{scene}_none_{sh}.png'
            if os.path.exists(p): row(f'{tag} {scene} none {sh}', off, load(p))
print('# 各 AA の出力フレームに新シェーダーを単体適用 (入力が同一なので厳密)')
for scene in ['photo', 'anime', 'stage']:
    for aa in ['none', 'smaa', 'taa0', 'taa05', 'dlssq', 'dlaa']:
        off = load(f'{cap}/new_{scene}_{aa}_off.png')
        for sh, kw in names.items():
            p = f'{cap}/probe_{scene}_{aa}_{kw}.png'
            if os.path.exists(p): row(f'probe {scene} {aa} {sh}', off, load(p))
print('# HDRP 実描画と単体適用の差 (座標ずれの検出用, 0..255)')
for scene in ['photo', 'anime']:
    for aa in ['none', 'taa05', 'dlssq', 'dlaa']:
        for sh, kw in names.items():
            a = np.asarray(Image.open(f'{cap}/new_{scene}_{aa}_{sh}.png').convert('RGB'), dtype=np.float64)
            b = np.asarray(Image.open(f'{cap}/probe_{scene}_{aa}_{kw}.png').convert('RGB'), dtype=np.float64)
            o = np.asarray(Image.open(f'{cap}/new_{scene}_{aa}_off.png').convert('RGB'), dtype=np.float64)
            print(f'{scene} {aa} {sh}: |HDRP-probe|={np.abs(a-b).mean():.3f}  |HDRP-off|={np.abs(a-o).mean():.3f}')
