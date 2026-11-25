import sys, os
from PIL import Image, ImageDraw, ImageFont
cap, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
font = ImageFont.truetype("arial.ttf", 15)
def grid(name, scene, roi, rows, zoom=4):
    # rows: [(rowlabel, [(collabel, filename-suffix-with-tag)])]
    x, y, w, h = roi; cw, ch = w * zoom, h * zoom
    ncol = max(len(r[1]) for r in rows)
    img = Image.new('RGB', (70 + (cw + 4) * ncol, (ch + 24) * len(rows)), (24, 24, 24))
    d = ImageDraw.Draw(img)
    for ri, (rl, cols) in enumerate(rows):
        d.text((3, ri * (ch + 24) + 30), rl, fill=(255, 255, 0), font=font)
        for ci, (cl, fn) in enumerate(cols):
            p = f'{cap}/{fn.format(scene=scene)}.png'
            d.text((74 + ci * (cw + 4), ri * (ch + 24) + 3), cl, fill=(255, 255, 255), font=font)
            if not os.path.exists(p): continue
            im = Image.open(p).convert('RGB').crop((x, y, x + w, y + h)).resize((cw, ch), Image.NEAREST)
            img.paste(im, (70 + ci * (cw + 4), ri * (ch + 24) + 22))
    img.save(f'{out}/{name}.png')
ROI = {
 'photo_chart': ('photo', (300, 395, 75, 55)), 'photo_fence': ('photo', (1230, 650, 75, 55)),
 'photo_wires': ('photo', (150, 205, 75, 55)), 'photo_bush': ('photo', (1470, 500, 75, 55)),
 'photo_brick': ('photo', (900, 360, 75, 55)), 'photo_sphere': ('photo', (690, 590, 75, 55)),
 'anime_line': ('anime', (200, 250, 75, 55)), 'anime_tone': ('anime', (430, 560, 75, 55)),
 'anime_face': ('anime', (885, 445, 75, 55)), 'anime_near': ('anime', (1650, 420, 75, 55)),
 'anime_far': ('anime', (1055, 480, 75, 55)),
 'stage_led': ('stage', (930, 500, 75, 55)), 'stage_light': ('stage', (900, 170, 75, 55)),
}
for k, (scene, roi) in ROI.items():
    grid('z1_' + k, scene, roi, [
        ('none', [('off', 'new_{scene}_none_off'), ('new CAS', 'new_{scene}_none_cas'), ('old CAS', 'old_{scene}_none_cas'), ('new RCAS', 'new_{scene}_none_rcas'), ('old RCAS', 'old_{scene}_none_rcas')]),
        ('none', [('off', 'new_{scene}_none_off'), ('new USM', 'new_{scene}_none_usm'), ('old USM', 'old_{scene}_none_usm'), ('new Adaptive', 'new_{scene}_none_adaptive'), ('old Adaptive', 'old_{scene}_none_adaptive')]),
    ])
    grid('z2_' + k, scene, roi, [(aa, [('off', 'new_{scene}_%s_off' % aa), ('CAS', 'new_{scene}_%s_cas' % aa), ('RCAS', 'new_{scene}_%s_rcas' % aa), ('USM', 'new_{scene}_%s_usm' % aa), ('Adaptive', 'new_{scene}_%s_adaptive' % aa)]) for aa in ['fxaa', 'smaa', 'taa0', 'taa05', 'dlssq']], zoom=3)
    grid('z3_' + k, scene, roi, [
        ('s=1', [('off', 'new_{scene}_none_off'), ('new CAS 1.0', 'new_{scene}_none_cas_s1'), ('old CAS 1.0', 'old_{scene}_none_cas_s1'), ('new USM 1.0', 'new_{scene}_none_usm_s1'), ('old USM 1.0', 'old_{scene}_none_usm_s1')]),
        ('pan', [('taa off', 'new_{scene}_taa05_off_pan'), ('taa CAS', 'new_{scene}_taa05_cas_pan'), ('taa Adaptive', 'new_{scene}_taa05_adaptive_pan'), ('dlss off', 'new_{scene}_dlssq_off_pan'), ('dlss CAS', 'new_{scene}_dlssq_cas_pan')]),
    ])
