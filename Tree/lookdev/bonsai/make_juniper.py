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

DARK = (34, 64, 44)
MID = (70, 114, 60)
FRESH = (152, 184, 92)


def shoot(rng, x, y, ang, length, depth, out):
    """A whipcord shoot from (x, y) at angle `ang` (radians, 0 = up): nearly straight, with short
    side shoots set at a narrow angle, alternately, like a frond. Appends (points, depth)."""
    n = max(3, int(length / 3))
    pts = [(x, y)]
    a = ang
    for _ in range(n):
        a += rng.uniform(-0.05, 0.05)
        x += math.sin(a) * length / n
        y -= math.cos(a) * length / n
        pts.append((x, y))
    out.append((pts, depth))
    if depth >= 3 or length < 9:
        return
    k = 0
    t = rng.uniform(0.1, 0.2)
    while t < 0.9:
        i = min(n - 1, int(t * n))
        side = 1 if k % 2 == 0 else -1
        sa = ang + side * math.radians(rng.uniform(22, 40))
        sl = length * rng.uniform(0.3, 0.46) * (1.0 - 0.5 * t)
        if sl > 5:
            shoot(rng, pts[i][0], pts[i][1], sa, sl, depth + 1, out)
        t += rng.uniform(0.06, 0.1) * (1.0 + depth * 0.3)
        k += 1


def scale_color(rng, t, depth, lift):
    """t: 0 at a shoot's base, 1 at its tip. Only the very tips are fresh; the body stays a deep
    blue-green, so a card reads as dark needles with light points, not as a leaf."""
    f = t * 0.7 + depth * 0.1 + lift * 0.3 + rng.uniform(-0.12, 0.12)
    f = min(1.0, max(0.0, f))
    if f < 0.55:
        a, b, u = DARK, MID, f / 0.55
    else:
        a, b, u = MID, FRESH, (f - 0.55) / 0.45
    g = rng.uniform(0.88, 1.1)
    return tuple(min(255, int((a[i] + (b[i] - a[i]) * u) * g)) for i in range(3))


def paint_cell(rng, col, height, ox, oy):
    """0.8.2 (look review: the lobed tufts read as broadleaf leaves at phone size): a tuft is now
    a loose fan of many thin, straight whipcord shoots with narrow side shoots, gaps between them,
    a bristly outline and only the tips light. At phone size it reads as fine needle texture."""
    lines = []
    bx = ox + CELL * 0.5
    by = oy + CELL * 0.97
    stem_len = CELL * rng.uniform(0.04, 0.07)
    sa = rng.uniform(-0.1, 0.1)
    sx = bx + math.sin(sa) * stem_len
    sy = by - math.cos(sa) * stem_len
    fans = rng.randint(15, 19)
    for f in range(fans):
        u = (f + rng.uniform(-0.35, 0.35)) / (fans - 1) - 0.5
        ang = sa + u * math.radians(104)
        reach = CELL * (0.7 - abs(u) * 0.3) * rng.uniform(0.75, 1.0)
        shoot(rng, sx, sy, ang, reach, 0, lines)
    dc = ImageDraw.Draw(col)
    dh = ImageDraw.Draw(height)
    dc.line([(bx, by), (sx, sy)], fill=(80, 52, 38, 255), width=4)
    cx0 = ox + CELL * 0.5
    cy0 = oy + CELL * 0.5
    for pts, depth in sorted(lines, key=lambda l: l[1]):
        n = len(pts)
        r0 = 3.4 - depth * 0.5
        for i in range(n - 1):
            t = i / max(1, n - 1)
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            seg = math.hypot(x1 - x0, y1 - y0)
            steps = max(1, int(seg / 1.6))
            ux = (x1 - x0) / max(seg, 1e-3)
            uy = (y1 - y0) / max(seg, 1e-3)
            for s in range(steps):
                u = s / steps
                x = x0 + (x1 - x0) * u
                y = y0 + (y1 - y0) * u
                r = max(1.1, r0 * (1.0 - 0.45 * t) * rng.uniform(0.85, 1.1))
                lift = min(1.0, math.hypot(x - cx0, y - cy0) / (CELL * 0.45)) * 0.7 + (1.0 - (y - oy) / CELL) * 0.3
                c = scale_color(rng, t, depth, lift)
                shade = tuple(int(v * 0.45) for v in c)
                # A scale leaf: a short pointed oval along the shoot, a dark rim under it.
                ex, ey = ux * r * 1.5, uy * r * 1.5
                px, py = -uy * r, ux * r
                rim = [(x - ex * 1.1, y - ey * 1.1), (x + px * 1.2, y + py * 1.2), (x + ex * 1.25, y + ey * 1.25), (x - px * 1.2, y - py * 1.2)]
                dc.polygon(rim, fill=shade + (255,))
                body = [(x - ex, y - ey), (x + px, y + py), (x + ex * 1.15, y + ey * 1.15), (x - px, y - py)]
                dc.polygon(body, fill=c + (255,))
                if rng.random() < 0.35:
                    hl = tuple(min(255, int(v * 1.3 + 10)) for v in c)
                    dc.line([(x, y), (x + ex * 0.8, y + ey * 0.8)], fill=hl + (255,), width=1)
                v = 110 + int(depth * 14) + int(lift * 40)
                dh.ellipse([x - r, y - r, x + r, y + r], fill=min(255, v))
                dh.point((x, y), fill=min(255, v + 40))
            # A needle point at the very tip of each shoot.
        (xa, ya), (xb, yb) = pts[-2], pts[-1]
        seg = math.hypot(xb - xa, yb - ya)
        if seg > 0:
            ux, uy = (xb - xa) / seg, (yb - ya) / seg
            tip = FRESH if rng.random() < 0.7 else MID
            dc.line([(xb, yb), (xb + ux * 4.5, yb + uy * 4.5)], fill=tip + (255,), width=1)


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
    fill = Image.new("RGB", (SIZE, SIZE), (36, 66, 44))
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
