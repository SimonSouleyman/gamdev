"""Paints four meadow grass clumps into one 1024 px atlas (2 x 2 cells).

Many thin arching blades per clump, darker and olive at the base, fresh green to the tips,
a few dry blades and seed heads, a light midrib. Drawn at twice the size and scaled down for
clean edges. Seeded. Run from Tree/: python3 lookdev/grass/make_grass.py (numpy + Pillow).
Output: lookdev/grass/grass_atlas.png (RGBA, root of each clump at the bottom centre).
"""
import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = "lookdev/grass/grass_atlas.png"
CELL = 1024  # drawn size of a cell (downscaled 2x)


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(len(a)))


def blade(draw, rng, base_x, cell_h, dry, seed_head):
    h = cell_h * rng.uniform(0.3, 0.78) ** 0.8
    # Blades lean out from the clump's middle and arch over near the top.
    lean = (base_x / CELL - 0.5) * rng.uniform(0.5, 1.1) + rng.uniform(-0.18, 0.18)
    arch = rng.uniform(0.0, 0.55) * (1 if lean >= 0 else -1)
    width = rng.uniform(5.0, 11.0)
    base = (base_x, cell_h - 2)
    if dry:
        c0, c1 = (92, 88, 52), (178, 164, 104)
    else:
        g = rng.uniform(0.85, 1.12)
        c0 = (38 * g, 56 * g, 22 * g)
        c1 = lerp((88, 128, 46), (104, 140, 70), rng.random())
        c1 = tuple(v * g for v in c1)
    steps = 40
    pts = []
    for i in range(steps + 1):
        t = i / steps
        # Arching curve: rises, then bends out and down with the arch.
        x = base[0] + (lean * t + arch * t * t * t) * h
        y = base[1] - h * (t - arch * 0.55 * t ** 3)
        pts.append((x, y))
    for i in range(steps):
        t = i / steps
        w = width * (1 - t) ** 0.8 + 1.2
        col = lerp(c0, c1, min(1.0, t * 1.4) ** 0.8)
        draw.line([pts[i], pts[i + 1]], fill=tuple(int(v) for v in col) + (255,), width=max(1, int(w)))
    # Midrib: a thin lighter line on the lower two thirds.
    if not dry:
        for i in range(int(steps * 0.65)):
            t = i / steps
            col = lerp(c0, c1, min(1.0, t * 1.4) ** 0.8)
            col = tuple(min(255, int(v * 1.25 + 8)) for v in col)
            draw.line([pts[i], pts[i + 1]], fill=col + (255,), width=1)
    if seed_head:
        top = pts[-1]
        stem_top = (top[0] + rng.uniform(-20, 20), max(40, top[1] - rng.uniform(40, 100)))
        draw.line([top, stem_top], fill=(120, 118, 70, 255), width=2)
        for k in range(18):
            a = rng.uniform(-2.4, -0.7)
            r = rng.uniform(6, 26)
            p = (stem_top[0] + math.cos(a) * r * 0.5, stem_top[1] + k * 3.2 - math.sin(a) * r * 0.2)
            draw.ellipse([p[0] - 3, p[1] - 5, p[0] + 3, p[1] + 5], fill=(150, 136, 90, 255))


def clump(rng, with_heads):
    img = Image.new("RGBA", (CELL, CELL), (60, 80, 30, 0))
    draw = ImageDraw.Draw(img)
    n = rng.randint(150, 190)
    # Back to front: long outer blades first, short fresh ones in front.
    blades = []
    for _ in range(n):
        bx = CELL * 0.5 + rng.gauss(0, CELL * 0.1)
        blades.append((bx, rng.random() < 0.08, with_heads and rng.random() < 0.04))
    rng.shuffle(blades)
    for bx, dry, head in blades:
        blade(draw, rng, bx, CELL, dry, head)
    # Fade the few blades that reach a cell edge, so no straight cut shows.
    arr = np.asarray(img).astype(np.float32)
    x = np.arange(CELL, dtype=np.float32)
    wx = np.clip(np.minimum(x, CELL - 1 - x) / 80.0, 0, 1)
    wy = np.clip(x / 80.0, 0, 1)
    arr[..., 3] *= wy[:, None] * wx[None, :]
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def main():
    rng = random.Random(7)
    atlas = Image.new("RGBA", (CELL, CELL), (60, 80, 30, 0))
    for cell in range(4):
        c = clump(rng, with_heads=cell >= 2).resize((CELL // 2, CELL // 2), Image.LANCZOS)
        atlas.paste(c, ((cell % 2) * CELL // 2, (cell // 2) * CELL // 2))
    # Bleed colour into transparent pixels so mipmaps do not fringe.
    arr = np.asarray(atlas).astype(np.float32)
    a = arr[..., 3:4] / 255.0
    rgb = Image.fromarray(arr[..., :3].astype(np.uint8)).filter(ImageFilter.GaussianBlur(8))
    wsum = np.asarray(Image.fromarray((a[..., 0] * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(8))).astype(np.float32)[..., None] / 255.0
    fill = np.where(wsum > 0.01, np.asarray(rgb).astype(np.float32) / np.maximum(wsum, 0.01), 50.0)
    out = arr[..., :3] * a + np.clip(fill, 0, 255) * (1 - a)
    Image.fromarray(np.concatenate([out, arr[..., 3:4]], -1).clip(0, 255).astype(np.uint8), "RGBA").save(OUT)


if __name__ == "__main__":
    main()
