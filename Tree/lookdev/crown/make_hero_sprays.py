"""Paints the hero tree's four leaf sprays into one 1024 px atlas (0.6.2, the tree rebuilt).

Each cell is the end of a shoot: a short twig with 7 to 10 large linden leaves fanning out
around its tip, so a spray reads as a clear bunch of leaves, not speckle. Compared with
make_sprays.py (still used by the forest): bigger leaves, a rounder, fuller silhouette, an
olive rather than neon green, and a clean edge (the colour is bled into the transparent
pixels premultiplied, so mipmaps never show a bright fringe).

Source leaves: ambientCG LeafSet004 (CC0), in assets/leaves. Seeded, so the output never
changes. Run from Tree/: python lookdev/crown/make_hero_sprays.py (numpy, scipy, Pillow).
Outputs lookdev/crown/hero_spray_color.png (RGBA) and hero_spray_normal.png.
"""
import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

SRC = "assets/leaves/LeafSet004_1K-JPG_"
OUT = "lookdev/crown/"
SIZE = 1024
CELL = SIZE // 2
BOXES = [(112, 43, 349, 497), (383, 45, 636, 506), (661, 63, 929, 479),
         (128, 542, 363, 981), (424, 554, 643, 980), (690, 561, 924, 976)]
# Mean leaf colour after grading (sRGB): an olive, slightly yellow green.
TARGET = np.array([84.0, 98.0, 44.0])


def grade(arr):
    """Source leaves are a saturated green (67, 99, 18): pull them to an olive green."""
    rgb = arr[..., :3]
    lum = rgb @ np.array([0.3, 0.59, 0.11])
    grey = np.repeat(lum[..., None], 3, axis=-1)
    out = grey + (rgb - grey) * 0.62
    mean_src = np.array([67.0, 99.0, 18.0])
    mean_mid = mean_src.dot([0.3, 0.59, 0.11]) + (mean_src - mean_src.dot([0.3, 0.59, 0.11])) * 0.62
    out = out * (TARGET / mean_mid)
    arr = arr.copy()
    arr[..., :3] = out
    return arr


def load_leaves():
    col = Image.open(SRC + "Color.jpg").convert("RGB")
    opa = Image.open(SRC + "Opacity.jpg").convert("L")
    leaves = []
    for b in BOXES:
        c = col.crop(b)
        m = np.asarray(opa.crop(b)) > 110
        lab, _n = ndimage.label(m)
        sizes = np.bincount(lab.ravel())
        sizes[0] = 0
        m = lab == sizes.argmax()
        # A crisp edge: barely blurred, so the alpha cut is clean.
        a = Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))
        arr = np.asarray(c.convert("RGBA")).astype(np.float32)
        arr = grade(arr)
        arr[..., 3] = np.asarray(a)
        leaves.append(Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA"))
    return leaves


def paint_spray(rng, leaves, color_out, normal_out):
    """A bunch of three shoots fanning out from the bottom centre, each with 6 to 8 leaves."""
    draw_c = ImageDraw.Draw(color_out)
    base = (CELL * 0.5, CELL * 0.98)
    n = 24
    placements = []
    shoots = []
    for j, fan in enumerate((-0.42, 0.0, 0.42)):
        lean = fan + rng.uniform(-0.08, 0.08)
        bend = rng.uniform(-0.1, 0.1) + fan * 0.2
        length = CELL * (0.62 if j == 1 else rng.uniform(0.42, 0.52))
        pts = []
        for i in range(n + 1):
            t = i / n
            pts.append((base[0] + (lean * t + bend * t * t) * length, base[1] - t * length))
        shoots.append(pts)
        count = rng.randint(6, 8)
        for k in range(count):
            t = 0.22 + 0.78 * math.sqrt((k + rng.uniform(0.0, 0.6)) / count)
            t = min(t, 0.99)
            i = min(int(t * n), n - 1)
            p = pts[i]
            tangent = math.atan2(pts[i + 1][1] - pts[i][1], pts[i + 1][0] - pts[i][0])
            side = 1 if k % 2 == 0 else -1
            spread = 95 - 65 * t
            ang = tangent + side * math.radians(rng.uniform(spread * 0.7, spread * 1.1))
            size = CELL * rng.uniform(0.27, 0.33) * (1.0 - 0.25 * t)
            placements.append((p, ang, size, rng.randrange(len(leaves))))
    for pts in shoots:
        for i in range(n):
            w = max(1, int(5 * (1 - i / n)) + 1)
            draw_c.line([pts[i], pts[i + 1]], fill=(70, 58, 40, 255), width=w)
    # Back to front in a shuffled order, so no shoot always lies on top.
    rng.shuffle(placements)
    for (p, ang, size, idx) in placements:
        c = leaves[idx]
        arr = np.asarray(c).astype(np.float32)
        # Per-leaf value and warmth: a sunlit leaf, a shaded one, a yellower one.
        g = rng.uniform(0.84, 1.14)
        warm = rng.uniform(-0.04, 0.1)
        arr[..., 0] *= g * (1 + warm)
        arr[..., 1] *= g
        arr[..., 2] *= g * (1 - warm)
        c2 = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA")
        scale = size / c.height
        w, h = max(2, int(c.width * scale)), max(2, int(c.height * scale))
        squash = rng.uniform(0.6, 1.0)
        c2 = c2.resize((max(2, int(w * squash)), h), Image.LANCZOS)
        deg = -math.degrees(ang) - 90
        rc = c2.rotate(deg, resample=Image.BICUBIC, expand=True)
        sx, sy = 0, c2.height / 2
        rad = math.radians(-deg)
        rx = sx * math.cos(rad) - sy * math.sin(rad)
        ry = sx * math.sin(rad) + sy * math.cos(rad)
        px = int(p[0] - (rc.width / 2 + rx))
        py = int(p[1] - (rc.height / 2 + ry))
        # A soft contact shadow of this leaf on the leaves painted before it.
        sh = rc.split()[3].filter(ImageFilter.GaussianBlur(7))
        shadow = Image.new("RGBA", rc.size, (12, 16, 6, 0))
        shadow.putalpha(sh.point(lambda v: int(v * 0.5)))
        box = (px + 6, py + 8, px + 6 + rc.width, py + 8 + rc.height)
        under = color_out.crop(box).split()[3]
        shadow.putalpha(Image.fromarray(np.minimum(np.asarray(shadow.split()[3]), np.asarray(under))))
        color_out.paste(Image.alpha_composite(color_out.crop(box), shadow), box[:2]) if box[0] >= 0 and box[1] >= 0 else None
        color_out.paste(Image.alpha_composite(color_out.crop((px, py, px + rc.width, py + rc.height)), rc), (px, py))
        tilt_x = rng.uniform(-0.45, 0.45)
        tilt_y = rng.uniform(-0.3, 0.45)
        nz = math.sqrt(max(0.05, 1 - tilt_x ** 2 - tilt_y ** 2))
        ncol = (int((tilt_x * 0.5 + 0.5) * 255), int((tilt_y * 0.5 + 0.5) * 255), int((nz * 0.5 + 0.5) * 255), 255)
        nl = Image.new("RGBA", rc.size, ncol)
        nl.putalpha(rc.split()[3])
        normal_out.paste(nl, (px, py), nl)


def bleed(img):
    """Fills the transparent pixels with the nearby leaf colour (premultiplied), keeps alpha."""
    arr = np.asarray(img).astype(np.float32)
    a = arr[..., 3:4] / 255.0
    pre = arr[..., :3] * a
    out = arr[..., :3].copy()
    fill = np.zeros_like(pre)
    got = np.zeros_like(a)
    for radius in (4, 12, 32, 96):
        bp = np.stack([ndimage.gaussian_filter(pre[..., k], radius) for k in range(3)], axis=-1)
        ba = ndimage.gaussian_filter(a[..., 0], radius)[..., None]
        colour = bp / np.maximum(ba, 1e-4)
        take = (ba > 1e-3) & (got < 0.5)
        fill = np.where(take, colour, fill)
        got = np.where(take, 1.0, got)
    fill = np.where(got > 0.5, fill, TARGET * 0.8)
    out = arr[..., :3] * a + fill * (1 - a)
    return Image.fromarray(np.clip(np.concatenate([out, arr[..., 3:4]], axis=-1), 0, 255).astype(np.uint8), "RGBA")


def main():
    rng = random.Random(2062)
    leaves = load_leaves()
    color = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    normal = Image.new("RGBA", (SIZE, SIZE), (128, 128, 255, 255))
    for cell in range(4):
        ox, oy = (cell % 2) * CELL, (cell // 2) * CELL
        layer = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        nlayer = Image.new("RGBA", (CELL, CELL), (128, 128, 255, 255))
        paint_spray(rng, leaves, layer, nlayer)
        color.paste(layer, (ox, oy))
        normal.paste(nlayer, (ox, oy))
    bleed(color).save(OUT + "hero_spray_color.png")
    normal.convert("RGB").save(OUT + "hero_spray_normal.png")


if __name__ == "__main__":
    main()
