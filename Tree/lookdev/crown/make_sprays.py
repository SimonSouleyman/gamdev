"""Paints four linden leaf sprays (a twig with many small leaves) into one 1024 px atlas.

Source leaves: ambientCG LeafSet004 (CC0), already in assets/leaves. Seeded, so the output
never changes. Run from Tree/: python3 lookdev/crown/make_sprays.py (needs numpy, scipy, Pillow).
Outputs lookdev/crown/leaf_spray_color.png (RGBA) and leaf_spray_normal.png.
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
# Leaf bounding boxes in the source atlas (x0, y0, x1, y1), stem at the bottom.
BOXES = [(112, 43, 349, 497), (383, 45, 636, 506), (661, 63, 929, 479),
         (128, 542, 363, 981), (424, 554, 643, 980), (690, 561, 924, 976)]


def load_leaves():
    col = Image.open(SRC + "Color.jpg").convert("RGB")
    opa = Image.open(SRC + "Opacity.jpg").convert("L")
    nor = Image.open(SRC + "NormalGL.jpg").convert("RGB")
    leaves = []
    for b in BOXES:
        c = col.crop(b)
        # Keep only the largest blob in the box (no neighbouring leaf or guide line).
        m = np.asarray(opa.crop(b)) > 110
        lab, _n = ndimage.label(m)
        sizes = np.bincount(lab.ravel())
        sizes[0] = 0
        m = lab == sizes.argmax()
        a = Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))
        c.putalpha(a)
        leaves.append((c, nor.crop(b)))
    return leaves


def paint_spray(rng, leaves, color_out, normal_out, ox, oy):
    """One spray in the cell at (ox, oy): twig from the bottom centre up and outward."""
    draw_c = ImageDraw.Draw(color_out)
    # Twig: a gently curved line, stem end at the bottom of the cell.
    base = (ox + CELL * 0.5, oy + CELL * 0.97)
    lean = rng.uniform(-0.18, 0.18)
    bend = rng.uniform(-0.12, 0.12)
    pts = []
    n = 24
    for i in range(n + 1):
        t = i / n
        x = base[0] + (lean * t + bend * t * t) * CELL
        y = base[1] - t * CELL * 0.82
        pts.append((x, y))
    # Leaves alternate left and right along the twig (linden is two-ranked), smaller at the tip.
    # Painted back to front: lower leaves first, each new one drops a soft shadow on the older ones.
    count = rng.randint(11, 15)
    placements = []
    for k in range(count):
        t = 0.08 + 0.9 * (k + rng.uniform(-0.2, 0.2)) / count
        t = min(max(t, 0.04), 0.98)
        i = min(int(t * n), n - 1)
        p = pts[i]
        tangent = math.atan2(pts[i + 1][1] - pts[i][1], pts[i + 1][0] - pts[i][0])
        side = 1 if k % 2 == 0 else -1
        # Leaf axis: out to the side and a bit forward; petiole leaves at 50-80 degrees.
        ang = tangent + side * math.radians(rng.uniform(45, 85))
        size = CELL * rng.uniform(0.26, 0.34) * (1.0 - 0.35 * t)
        placements.append((p, ang, size, rng.randrange(len(leaves)), rng))
    # The twig itself, under the leaves.
    for i in range(n):
        w = max(1, int(5 * (1 - i / n)) + 1)
        draw_c.line([pts[i], pts[i + 1]], fill=(74, 60, 38, 255), width=w)
    for (p, ang, size, idx, _r) in placements:
        c, nm = leaves[idx]
        # Per-leaf colour: brighter young leaves near the tip, some yellowish, some darker.
        arr = np.asarray(c).astype(np.float32)
        g = rng.uniform(0.82, 1.12)
        warm = rng.uniform(-0.05, 0.08)
        arr[..., 0] *= g * (1 + warm)
        arr[..., 1] *= g
        arr[..., 2] *= g * (1 - warm * 1.5)
        c2 = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA")
        scale = size / c.height
        w, h = max(2, int(c.width * scale)), max(2, int(c.height * scale))
        # Foreshortening: leaves tilt towards and away from the viewer.
        squash = rng.uniform(0.55, 1.0)
        c2 = c2.resize((max(2, int(w * squash)), h), Image.LANCZOS)
        # Leaf image points up (stem at the bottom): rotate so the stem sits on the twig.
        deg = -math.degrees(ang) - 90
        rc = c2.rotate(deg, resample=Image.BICUBIC, expand=True)
        # Stem end in rotated space: the leaf's bottom centre, rotated about the image centre.
        cx, cy = c2.width / 2, c2.height / 2
        sx, sy = 0, c2.height / 2
        rad = math.radians(-deg)
        rx = sx * math.cos(rad) - sy * math.sin(rad)
        ry = sx * math.sin(rad) + sy * math.cos(rad)
        px = int(p[0] - (rc.width / 2 + rx))
        py = int(p[1] - (rc.height / 2 + ry))
        # Soft contact shadow of this leaf on what is already painted.
        sh = rc.split()[3].filter(ImageFilter.GaussianBlur(6))
        shadow = Image.new("RGBA", rc.size, (10, 20, 5, 0))
        shadow.putalpha(sh.point(lambda v: int(v * 0.45)))
        region = color_out.crop((px + 5, py + 7, px + 5 + rc.width, py + 7 + rc.height))
        under_alpha = region.split()[3]
        shadow.putalpha(Image.fromarray(np.minimum(np.asarray(shadow.split()[3]), np.asarray(under_alpha))))
        color_out.alpha_composite(shadow, (max(px + 5, 0), max(py + 7, 0)) if px + 5 >= 0 and py + 7 >= 0 else (0, 0))
        color_out.alpha_composite(rc, (px, py)) if px >= 0 and py >= 0 else color_out.paste(rc, (px, py), rc)
        # Normal: each leaf gets its own tilt, so the spray shades leaf by leaf.
        tilt_x = rng.uniform(-0.45, 0.45)
        tilt_y = rng.uniform(-0.3, 0.45)
        nz = math.sqrt(max(0.05, 1 - tilt_x ** 2 - tilt_y ** 2))
        ncol = (int((tilt_x * 0.5 + 0.5) * 255), int((tilt_y * 0.5 + 0.5) * 255), int((nz * 0.5 + 0.5) * 255), 255)
        nl = Image.new("RGBA", rc.size, ncol)
        nl.putalpha(rc.split()[3])
        normal_out.paste(nl, (px, py), nl)


def main():
    rng = random.Random(2026)
    leaves = load_leaves()
    color = Image.new("RGBA", (SIZE, SIZE), (46, 66, 26, 0))
    normal = Image.new("RGBA", (SIZE, SIZE), (128, 128, 255, 255))
    for cell in range(4):
        ox, oy = (cell % 2) * CELL, (cell // 2) * CELL
        layer = Image.new("RGBA", (CELL, CELL), (46, 66, 26, 0))
        nlayer = Image.new("RGBA", (CELL, CELL), (128, 128, 255, 255))
        paint_spray(rng, leaves, layer, nlayer, 0, 0)
        color.paste(layer, (ox, oy))
        normal.paste(nlayer, (ox, oy))
    # Bleed the leaf colour into the transparent pixels, so mipmaps do not fringe dark.
    arr = np.asarray(color).astype(np.float32)
    a = arr[..., 3:4] / 255.0
    rgb = Image.fromarray(np.clip(arr[..., :3], 0, 255).astype(np.uint8))
    blurred = np.asarray(rgb.filter(ImageFilter.GaussianBlur(12))).astype(np.float32)
    wsum = np.asarray(Image.fromarray((a[..., 0] * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(12))).astype(np.float32)[..., None] / 255.0
    fill = np.where(wsum > 0.01, blurred / np.maximum(wsum, 0.01), np.array([46, 66, 26], np.float32))
    out_rgb = arr[..., :3] * a + np.clip(fill, 0, 255) * (1 - a)
    out = np.concatenate([out_rgb, arr[..., 3:4]], axis=-1)
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA").save(OUT + "leaf_spray_color.png")
    normal.convert("RGB").save(OUT + "leaf_spray_normal.png")


if __name__ == "__main__":
    main()
