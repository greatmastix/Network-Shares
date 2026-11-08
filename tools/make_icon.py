"""Builds src/NetworkShares/app.ico: the app's dark tile with a red folder on a network link.
Usage: python3 tools/make_icon.py src/NetworkShares/app.ico preview.png"""
import sys
from PIL import Image, ImageDraw

BG, BORDER, RED, DARK = (21, 21, 23, 255), (42, 42, 47, 255), (239, 75, 85, 255), (150, 34, 42, 255)

def draw(size):
    S = 1024                                  # draw big, downsample: smooth edges at every size
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    small = size <= 32
    pad = 8 if small else 48
    d.rounded_rectangle([pad, pad, S - pad, S - pad], radius=190, fill=BG, outline=None if small else BORDER, width=0 if small else 18)
    if small:   # 16-32 px: just a bold folder, centred
        x0, x1, top, y1 = 150, 874, 250, 790
    else:
        x0, x1, top, y1 = 230, 794, 220, 640
    tab_w, tab_h, r = (x1 - x0) * 0.42, (y1 - top) * 0.16, 48 if small else 40
    # Back of the folder (darker red) with its tab, then the front panel.
    d.rounded_rectangle([x0, top, x0 + tab_w, top + tab_h * 2], radius=r, fill=DARK)
    d.rounded_rectangle([x0, top + tab_h, x1, y1], radius=r, fill=DARK)
    d.rounded_rectangle([x0, top + tab_h * 2.1, x1, y1], radius=r, fill=RED)
    if not small:   # the network: a stem down to a bar with three nodes
        cx, w = S // 2, 30
        d.rectangle([cx - w // 2, y1, cx + w // 2, 760], fill=RED)
        d.rounded_rectangle([250, 745, 774, 775], radius=15, fill=RED)
        for nx in (250, cx, 774):
            d.ellipse([nx - 52, 708, nx + 52, 812], fill=RED, outline=BG, width=22)
    return im.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
imgs = [draw(s) for s in sizes]
imgs[-1].save(sys.argv[1], format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
sheet = Image.new("RGBA", (sum(sizes) + 20 * len(sizes) + 20, 300), (12, 12, 14, 255))
x = 20
for s, im in zip(sizes, imgs):
    sheet.alpha_composite(im, (x, 150 - s // 2)); x += s + 20
sheet.save(sys.argv[2])
