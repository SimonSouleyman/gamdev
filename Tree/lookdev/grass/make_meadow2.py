"""Paints a second meadow atlas (2 x 2 cells) for more variety (Simon, play test 4):
0 short fine grass, 1 a blue-green sedge tuft, 2 a clover patch with a few white heads,
3 meadow flowers (buttercups, daisies, a cornflower) among a few blades.
Drawn at twice the size and scaled down. Seeded. Run from Tree/:
python lookdev/grass/make_meadow2.py (Pillow only). Output: lookdev/grass/meadow_atlas2.png.
"""
import math
import random

from PIL import Image, ImageChops, ImageDraw

OUT = "lookdev/grass/meadow_atlas2.png"
CELL = 1024


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(len(a)))


def blade(draw, rng, base_x, h, width, c0, c1, lean, arch):
    steps = 30
    pts = []
    for i in range(steps + 1):
        t = i / steps
        pts.append((base_x + (lean * t + arch * t ** 3) * h, CELL - 2 - h * (t - arch * 0.5 * t ** 3)))
    for i in range(steps):
        t = i / steps
        w = width * (1 - t) ** 0.8 + 1.0
        col = lerp(c0, c1, min(1.0, t * 1.4) ** 0.8)
        draw.line([pts[i], pts[i + 1]], fill=tuple(int(v) for v in col) + (255,), width=max(1, int(w)))
    return pts[-1]


def fine_grass(rng):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for _ in range(300):
        bx = CELL * 0.5 + rng.gauss(0, CELL * 0.13)
        h = CELL * rng.uniform(0.18, 0.46)
        g = rng.uniform(0.85, 1.15)
        c0 = (44 * g, 64 * g, 26 * g)
        c1 = tuple(v * g for v in lerp((112, 150, 58), (136, 160, 78), rng.random()))
        lean = (bx / CELL - 0.5) * rng.uniform(0.6, 1.2) + rng.uniform(-0.2, 0.2)
        blade(d, rng, bx, h, rng.uniform(2.5, 5.0), c0, c1, lean, rng.uniform(-0.3, 0.3))
    return img


def sedge(rng):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for _ in range(46):
        bx = CELL * 0.5 + rng.gauss(0, CELL * 0.06)
        h = CELL * rng.uniform(0.45, 0.85)
        g = rng.uniform(0.9, 1.1)
        c0 = (40 * g, 58 * g, 40 * g)
        c1 = (92 * g, 124 * g, 96 * g)
        lean = (bx / CELL - 0.5) * 1.6 + rng.uniform(-0.25, 0.25)
        blade(d, rng, bx, h, rng.uniform(10, 18), c0, c1, lean, rng.uniform(0.0, 0.35) * (1 if lean > 0 else -1))
    return img


def trefoil(d, rng, cx, cy, r, col):
    for k in range(3):
        a = -math.pi / 2 + k * 2 * math.pi / 3 + rng.uniform(-0.2, 0.2)
        x, y = cx + math.cos(a) * r * 0.9, cy + math.sin(a) * r * 0.55
        d.ellipse([x - r, y - r * 0.7, x + r, y + r * 0.7], fill=col + (255,))
    # The pale chevron on each leaflet.
    d.ellipse([cx - r * 0.3, cy - r * 0.2, cx + r * 0.3, cy + r * 0.2], fill=tuple(min(255, int(v * 1.3)) for v in col) + (255,))


def clover(rng):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    leaves = []
    for _ in range(70):
        cx = CELL * 0.5 + rng.gauss(0, CELL * 0.16)
        cy = CELL - rng.uniform(40, CELL * 0.32)
        leaves.append((cy, cx))
    leaves.sort()
    for cy, cx in leaves:
        d.line([(cx, CELL - 4), (cx + rng.uniform(-10, 10), cy)], fill=(70, 100, 40, 255), width=4)
        g = rng.uniform(0.85, 1.1)
        trefoil(d, rng, cx, cy, rng.uniform(34, 50), (int(58 * g), int(110 * g), int(46 * g)))
    for _ in range(6):
        cx = CELL * 0.5 + rng.gauss(0, CELL * 0.14)
        top = CELL - rng.uniform(CELL * 0.3, CELL * 0.45)
        d.line([(cx, CELL - 4), (cx, top)], fill=(80, 110, 50, 255), width=5)
        for k in range(40):
            a = rng.uniform(0, 2 * math.pi)
            rr = rng.uniform(0, 40)
            x, y = cx + math.cos(a) * rr, top + math.sin(a) * rr * 0.8
            shade = rng.uniform(0.85, 1.0)
            d.ellipse([x - 9, y - 12, x + 9, y + 12], fill=(int(240 * shade), int(236 * shade), int(226 * shade), 255))
    return img


def flower_head(d, rng, x, y, kind, s=3.0):
    if kind == "buttercup":
        for i in range(5):
            a = i * 2 * math.pi / 5 + rng.uniform(-0.1, 0.1)
            px, py = x + math.cos(a) * 13 * s, y + math.sin(a) * 10 * s
            d.ellipse([px - 11 * s, py - 9 * s, px + 11 * s, py + 9 * s], fill=(246, 208, 40, 255))
        d.ellipse([x - 6 * s, y - 5 * s, x + 6 * s, y + 5 * s], fill=(200, 150, 30, 255))
    elif kind == "daisy":
        for i in range(14):
            a = i * 2 * math.pi / 14
            px, py = x + math.cos(a) * 17 * s, y + math.sin(a) * 12 * s
            d.line([(x, y), (px, py)], fill=(250, 250, 246, 255), width=int(7 * s))
        d.ellipse([x - 8 * s, y - 6 * s, x + 8 * s, y + 6 * s], fill=(236, 190, 40, 255))
    else:  # cornflower
        for i in range(10):
            a = i * 2 * math.pi / 10
            px, py = x + math.cos(a) * 15 * s, y + math.sin(a) * 11 * s
            d.ellipse([px - 8 * s, py - 6 * s, px + 8 * s, py + 6 * s], fill=(70, 100, 210, 255))
        d.ellipse([x - 6 * s, y - 5 * s, x + 6 * s, y + 5 * s], fill=(60, 50, 140, 255))


def flowers(rng):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for _ in range(60):
        bx = CELL * 0.5 + rng.gauss(0, CELL * 0.12)
        blade(d, rng, bx, CELL * rng.uniform(0.2, 0.5), rng.uniform(3, 6), (44, 64, 26), (116, 150, 60),
              (bx / CELL - 0.5) + rng.uniform(-0.2, 0.2), rng.uniform(-0.3, 0.3))
    kinds = ["buttercup"] * 4 + ["daisy"] * 3 + ["cornflower"] * 2
    for kind in kinds:
        bx = CELL * 0.5 + rng.gauss(0, CELL * 0.14)
        top = blade(d, rng, bx, CELL * rng.uniform(0.3, 0.6), 7.0, (60, 90, 40), (90, 130, 60),
                    rng.uniform(-0.2, 0.2), rng.uniform(-0.1, 0.1))
        flower_head(d, rng, top[0], top[1], kind)
    return img


def edge_mask():
    # Fades whatever reaches a cell edge, so no straight cut shows.
    m = Image.new("L", (CELL, CELL), 0)
    px = m.load()
    for y in range(CELL):
        wy = min(1.0, y / 60.0)
        for x in range(CELL):
            wx = min(1.0, min(x, CELL - 1 - x) / 60.0)
            px[x, y] = int(255 * wx * wy)
    return m


def main():
    rng = random.Random(11)
    mask = edge_mask()
    atlas = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    for cell, make in enumerate([fine_grass, sedge, clover, flowers]):
        c = make(rng)
        c.putalpha(ImageChops.multiply(c.getchannel("A"), mask))
        c = c.resize((CELL // 2, CELL // 2), Image.LANCZOS)
        atlas.paste(c, ((cell % 2) * CELL // 2, (cell // 2) * CELL // 2))
    # A grass-green under the transparent pixels, so mipmaps do not fringe dark.
    under = Image.new("RGB", atlas.size, (74, 98, 42))
    under.paste(atlas.convert("RGB"), mask=atlas.getchannel("A"))
    under.putalpha(atlas.getchannel("A"))
    under.save(OUT)


if __name__ == "__main__":
    main()
