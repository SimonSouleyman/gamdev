"""Paints four juniper foliage sprays (scale-like leaves on fine cord-like shoots) into one
1024 px atlas, laid out like the linden sprays (lookdev/crown): 2 x 2 cells, the stem end at
the bottom centre of each cell. Pillow only, seeded, so the output never changes.
Run from Tree/: python lookdev/bonsai/make_juniper.py
Outputs lookdev/bonsai/juniper_spray_color.png (RGBA) and juniper_spray_normal.png.
"""
import math
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = "lookdev/bonsai/"
SIZE = 1024
CELL = SIZE // 2


def shoot(rng, x, y, ang, length, depth, out):
    """A cord-like shoot from (x, y) at angle `ang` (radians, 0 = up), branching pinnately.
    Appends polylines (list of points, depth) to `out`."""
    n = max(4, int(length / 6))
    pts = [(x, y)]
    a = ang
    for i in range(n):
        a += rng.uniform(-0.08, 0.08)
        x += math.sin(a) * length / n
        y -= math.cos(a) * length / n
        pts.append((x, y))
    out.append((pts, depth))
    if depth >= 3:
        return
    # Side shoots alternate left and right, shorter toward the tip.
    k = 0
    t = rng.uniform(0.12, 0.2)
    while t < 0.9:
        i = int(t * n)
        side = 1 if k % 2 == 0 else -1
        sa = ang + side * math.radians(rng.uniform(38, 62))
        sl = length * rng.uniform(0.45, 0.62) * (1.0 - 0.5 * t)
        if sl > 10:
            shoot(rng, pts[i][0], pts[i][1], sa, sl, depth + 1, out)
        t += rng.uniform(0.06, 0.1) * (1.0 + depth * 0.3)
        k += 1


def scale_color(rng, t, depth):
    """Older scales near the stem are darker and bluer, the fresh tips bright and yellowish."""
    dark = (38, 70, 40)
    fresh = (118, 156, 64)
    f = min(1.0, max(0.0, t * 0.8 + depth * 0.12 + rng.uniform(-0.12, 0.12)))
    c = [int(dark[i] + (fresh[i] - dark[i]) * f) for i in range(3)]
    g = rng.uniform(0.9, 1.08)
    return tuple(min(255, int(v * g)) for v in c)


def paint_cell(rng, col, height, ox, oy):
    lines = []
    base = (ox + CELL * 0.5, oy + CELL * 0.97)
    shoot(rng, base[0], base[1], rng.uniform(-0.15, 0.15), CELL * 0.86, 0, lines)
    dc = ImageDraw.Draw(col)
    dh = ImageDraw.Draw(height)
    # The woody stem of the spray first, thin and red-brown.
    main = lines[0][0]
    for i in range(len(main) - 1):
        w = max(1, int(6 * (1 - i / len(main))))
        dc.line([main[i], main[i + 1]], fill=(92, 60, 42, 255), width=w)
    # Scales: small overlapping teardrops pressed along every shoot, back to front (base first).
    for pts, depth in sorted(lines, key=lambda l: l[1]):
        n = len(pts)
        r0 = 9.5 - depth * 1.7
        for i in range(n - 1):
            t = i / max(1, n - 1)
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            seg = math.hypot(x1 - x0, y1 - y0)
            steps = max(1, int(seg / 2.2))
            for s in range(steps):
                u = s / steps
                x = x0 + (x1 - x0) * u
                y = y0 + (y1 - y0) * u
                r = max(1.6, r0 * (1.0 - 0.55 * t) * rng.uniform(0.85, 1.1))
                off = rng.uniform(-0.5, 0.5) * r
                ax = -(y1 - y0) / max(seg, 1e-3)
                ay = (x1 - x0) / max(seg, 1e-3)
                cx = x + ax * off
                cy = y + ay * off
                c = scale_color(rng, t, depth)
                dc.ellipse([cx - r, cy - r * 1.25, cx + r, cy + r * 1.25], fill=c + (255,))
                # A lighter edge on the upper side of each scale.
                hl = tuple(min(255, int(v * 1.25)) for v in c)
                dc.ellipse([cx - r * 0.55, cy - r * 1.1, cx + r * 0.3, cy - r * 0.2], fill=hl + (255,))
                # Height: every scale a small dome.
                for k in range(3):
                    rr = r * (1.0 - k * 0.3)
                    v = 110 + k * 50 + int(depth * 8)
                    dh.ellipse([cx - rr, cy - rr * 1.2, cx + rr, cy + rr * 1.2], fill=v)


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
            paint_cell(rng, col, height, cx * CELL, cy * CELL)
    # Soft alpha edges; transparent pixels keep a leaf colour so mipmaps do not bleed black.
    alpha = col.getchannel("A").filter(ImageFilter.GaussianBlur(0.6))
    rgb = col.convert("RGB")
    fill = Image.new("RGB", (SIZE, SIZE), (52, 84, 44))
    rgb = Image.composite(rgb, fill, col.getchannel("A"))
    out = rgb.copy()
    out.putalpha(alpha)
    out.save(OUT + "juniper_spray_color.png")
    normal_from_height(height).save(OUT + "juniper_spray_normal.png")


if __name__ == "__main__":
    main()
