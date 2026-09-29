"""Paints four juniper foliage tufts into one 1024 px atlas, laid out like the linden sprays
(lookdev/crown): 2 x 2 cells, the stem end at the bottom centre of each cell.

A tuft is what a pinched juniper grows at the end of a branch: a short woody stem that fans out
into many cord-like shoots, each densely clad in tiny overlapping scale leaves, with lots of
short side shoots, so the whole thing reads as a compact, lobed little cloud (not a leafy twig).
Old scales inside are dark blue-green, the fresh tips light yellow-green. Pads in the game are
built from many of these tufts.

Pillow only, seeded, so the output never changes.
Run from Tree/: python lookdev/bonsai/make_juniper.py
Outputs lookdev/bonsai/juniper_spray_color.png (RGBA) and juniper_spray_normal.png, and the
half-size copy of the crown's leaf sprays for the broadleaf bonsai (leaf_spray_small_*.png).
"""
import math
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = "lookdev/bonsai/"
SIZE = 1024
CELL = SIZE // 2

DARK = (30, 58, 38)
MID = (62, 104, 46)
FRESH = (150, 178, 72)


def shoot(rng, x, y, ang, length, depth, out):
    """A cord-like shoot from (x, y) at angle `ang` (radians, 0 = up), branching pinnately and
    densely. Appends (points, depth) polylines to `out`."""
    n = max(3, int(length / 4))
    pts = [(x, y)]
    a = ang
    for _ in range(n):
        a += rng.uniform(-0.1, 0.1)
        x += math.sin(a) * length / n
        y -= math.cos(a) * length / n
        pts.append((x, y))
    out.append((pts, depth))
    if depth >= 3 or length < 12:
        return
    k = 0
    t = rng.uniform(0.08, 0.16)
    while t < 0.88:
        i = min(n - 1, int(t * n))
        side = 1 if k % 2 == 0 else -1
        sa = ang + side * math.radians(rng.uniform(30, 55))
        sl = length * rng.uniform(0.38, 0.55) * (1.0 - 0.45 * t)
        if sl > 7:
            shoot(rng, pts[i][0], pts[i][1], sa, sl, depth + 1, out)
        t += rng.uniform(0.07, 0.12) * (1.0 + depth * 0.25)
        k += 1


def scale_color(rng, t, depth, lift):
    """t: 0 at a shoot's base, 1 at its tip. Tips of the outer shoots are the freshest."""
    f = t * 0.75 + depth * 0.1 + lift * 0.35 + rng.uniform(-0.15, 0.15)
    f = min(1.0, max(0.0, f))
    if f < 0.5:
        a, b, u = DARK, MID, f / 0.5
    else:
        a, b, u = MID, FRESH, (f - 0.5) / 0.5
    g = rng.uniform(0.9, 1.08)
    return tuple(min(255, int((a[i] + (b[i] - a[i]) * u) * g)) for i in range(3))


def paint_cell(rng, col, height, ox, oy):
    lines = []
    bx = ox + CELL * 0.5
    by = oy + CELL * 0.96
    # A short stem, then a fan of main shoots: the tuft is a rounded lobed cloud.
    stem_len = CELL * rng.uniform(0.1, 0.16)
    sa = rng.uniform(-0.15, 0.15)
    sx = bx + math.sin(sa) * stem_len
    sy = by - math.cos(sa) * stem_len
    fans = rng.randint(8, 10)
    for f in range(fans):
        u = (f + rng.uniform(-0.3, 0.3)) / (fans - 1) - 0.5
        ang = sa + u * math.radians(112)
        # The middle shoots reach furthest: a round top.
        reach = CELL * (0.74 - abs(u) * 0.4) * rng.uniform(0.85, 1.0)
        shoot(rng, sx, sy, ang, reach * 0.9, 0, lines)
    dc = ImageDraw.Draw(col)
    dh = ImageDraw.Draw(height)
    # The woody stem, red-brown, mostly hidden under the scales.
    dc.line([(bx, by), (sx, sy)], fill=(96, 58, 40, 255), width=7)
    cx0 = ox + CELL * 0.5
    cy0 = oy + CELL * 0.45
    # Scales, base shoots first so the fresh outer shoots lie on top.
    for pts, depth in sorted(lines, key=lambda l: l[1]):
        n = len(pts)
        r0 = 5.0 - depth * 0.8
        for i in range(n - 1):
            t = i / max(1, n - 1)
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            seg = math.hypot(x1 - x0, y1 - y0)
            steps = max(1, int(seg / 1.4))
            for s in range(steps):
                u = s / steps
                x = x0 + (x1 - x0) * u
                y = y0 + (y1 - y0) * u
                r = max(1.8, r0 * (1.0 - 0.5 * t) * rng.uniform(0.85, 1.12))
                ax = -(y1 - y0) / max(seg, 1e-3)
                ay = (x1 - x0) / max(seg, 1e-3)
                off = rng.uniform(-0.55, 0.55) * r
                cx = x + ax * off
                cy = y + ay * off
                # Outer shoots (far from the tuft's middle, and higher) are lighter.
                lift = min(1.0, math.hypot(cx - cx0, cy - cy0) / (CELL * 0.45)) * 0.6 + (1.0 - (cy - oy) / CELL) * 0.4
                c = scale_color(rng, t, depth, lift)
                shade = tuple(int(v * 0.55) for v in c)
                dc.ellipse([cx - r * 1.08, cy - r * 1.3, cx + r * 1.08, cy + r * 1.3], fill=shade + (255,))
                dc.ellipse([cx - r, cy - r * 1.2, cx + r, cy + r * 1.2], fill=c + (255,))
                hl = tuple(min(255, int(v * 1.22 + 8)) for v in c)
                dc.ellipse([cx - r * 0.55, cy - r * 1.05, cx + r * 0.25, cy - r * 0.25], fill=hl + (255,))
                for k in range(3):
                    rr = r * (1.0 - k * 0.3)
                    v = 100 + k * 45 + int(depth * 10) + int(lift * 30)
                    dh.ellipse([cx - rr, cy - rr * 1.2, cx + rr, cy + rr * 1.2], fill=min(255, v))


def normal_from_height(h):
    """A tangent-space normal map (OpenGL, green up) from a height image, Pillow kernels only."""
    h = h.filter(ImageFilter.GaussianBlur(1.2))
    dx = h.filter(ImageFilter.Kernel((3, 3), [-1, 0, 1, -2, 0, 2, -1, 0, 1], scale=2, offset=128))
    dy = h.filter(ImageFilter.Kernel((3, 3), [1, 2, 1, 0, 0, 0, -1, -2, -1], scale=2, offset=128))
    r = ImageChops.invert(dx)
    g = dy
    b = Image.new("L", h.size, 235)
    return Image.merge("RGB", (r, g, b))


def main():
    rng = random.Random(1609)
    col = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    height = Image.new("L", (SIZE, SIZE), 0)
    for cy in range(2):
        for cx in range(2):
            c = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
            h = Image.new("L", (SIZE, SIZE), 0)
            paint_cell(rng, c, h, cx * CELL, cy * CELL)
            box = (cx * CELL + 4, cy * CELL + 4, cx * CELL + CELL - 4, cy * CELL + CELL - 2)
            col.paste(c.crop(box), box[:2])
            height.paste(h.crop(box), box[:2])
    # Soft alpha edges; transparent pixels keep a leaf colour so mipmaps do not bleed black.
    alpha = col.getchannel("A").filter(ImageFilter.GaussianBlur(0.7))
    rgb = col.convert("RGB")
    fill = Image.new("RGB", (SIZE, SIZE), (48, 80, 42))
    rgb = Image.composite(rgb, fill, col.getchannel("A"))
    out = rgb.copy()
    out.putalpha(alpha)
    out.save(OUT + "juniper_spray_color.png")
    normal_from_height(height).save(OUT + "juniper_spray_normal.png")


def leaf_copy():
    """The roster's leaf sprays for the broadleaf bonsai: the crown atlas (lookdev/crown) at half
    size, imported with mipmaps so the small leaves do not shimmer on the sill."""
    for kind in ("color", "normal"):
        im = Image.open("lookdev/crown/leaf_spray_%s.png" % kind)
        im.resize((im.width // 2, im.height // 2), Image.LANCZOS).save(OUT + "leaf_spray_small_%s.png" % kind)


if __name__ == "__main__":
    main()
    leaf_copy()
