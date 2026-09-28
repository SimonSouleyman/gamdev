"""Paints the understory atlas (2 x 2 cells) for the living clearing (design doc 17.6): the shade
plants that come up under a grown crown, in the style of make_meadow2.py.
0 wood anemone (a few plants, white six-petalled flowers over three-lobed leaves),
1 a fern (arching fronds from one crown), 2 a cushion of moss seen from above (it lies flat on
the ground), 3 a small group of brown-capped mushrooms in leaf litter.
Drawn at twice the size and scaled down. Seeded. Run from Tree/:
python lookdev/grass/make_understory.py (Pillow only). Output: lookdev/grass/understory_atlas.png.
"""
import math
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter

from make_meadow2 import CELL, blade, edge_mask, lerp

OUT = "lookdev/grass/understory_atlas.png"


def col(c, a=255):
    return tuple(int(max(0, min(255, v))) for v in c) + (a,)


def anemone_leaf(d, rng, cx, cy, r, green):
    # Three deeply cut leaflets, each a fan of narrow lobes.
    for k in range(3):
        a = -math.pi / 2 + (k - 1) * 1.15 + rng.uniform(-0.15, 0.15)
        for j in range(4):
            aa = a + (j - 1.5) * 0.22
            x1 = cx + math.cos(aa) * r
            y1 = cy + math.sin(aa) * r * 0.5
            mx = cx + math.cos(aa) * r * 0.5
            my = cy + math.sin(aa) * r * 0.25
            w = r * 0.16
            px, py = -math.sin(aa) * w, math.cos(aa) * w * 0.5
            g = rng.uniform(0.85, 1.12)
            d.polygon([(cx, cy), (mx + px, my + py), (x1, y1), (mx - px, my - py)], fill=col(tuple(v * g for v in green)))
        # The midrib, a little lighter.
        d.line([(cx, cy), (cx + math.cos(a) * r * 0.8, cy + math.sin(a) * r * 0.4)], fill=col(tuple(v * 1.3 for v in green)), width=3)


def anemone_flower(d, rng, x, y, s, nod):
    # Six (sometimes seven) white tepals, pink-flushed on the outside, a yellow boss of stamens.
    n = 6 if rng.random() < 0.8 else 7
    squash = 0.55 + 0.3 * (1 - nod)
    for i in range(n):
        a = i * 2 * math.pi / n + rng.uniform(-0.1, 0.1)
        px, py = x + math.cos(a) * 16 * s, y + math.sin(a) * 16 * s * squash
        pink = rng.uniform(0.0, 0.25) if math.sin(a) > 0.2 else 0.0
        c = lerp((248, 247, 240), (226, 180, 196), pink)
        d.ellipse([px - 12 * s, py - 9 * s * squash - 3, px + 12 * s, py + 9 * s * squash + 3], fill=col(c))
    for i in range(14):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0, 6 * s)
        sx, sy = x + math.cos(a) * rr, y + math.sin(a) * rr * squash
        d.ellipse([sx - 2.5 * s, sy - 2.5 * s, sx + 2.5 * s, sy + 2.5 * s], fill=col((236, 196, 50)))


def anemones(rng):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # A carpet of leaves first, then the flowering plants over it.
    for _ in range(26):
        x = CELL * 0.5 + rng.gauss(0, CELL * 0.17)
        y = CELL - rng.uniform(30, CELL * 0.18)
        d.line([(x, CELL - 4), (x, y)], fill=col((80, 100, 50)), width=4)
        anemone_leaf(d, rng, x, y, rng.uniform(70, 110), (40, 84, 34))
    plants = sorted([(rng.uniform(0.28, 0.55), CELL * 0.5 + rng.gauss(0, CELL * 0.2)) for _ in range(14)])
    for h, bx in plants:
        top_y = CELL - CELL * h
        lean = rng.uniform(-60, 60)
        # The stem, the whorl of three leaves two thirds up, the flower on a short stalk above.
        wy = CELL - (CELL - top_y) * 0.62
        d.line([(bx, CELL - 4), (bx + lean * 0.6, wy)], fill=col((104, 92, 70)), width=6)
        anemone_leaf(d, rng, bx + lean * 0.6, wy, rng.uniform(80, 120), (46, 92, 38))
        fx, fy = bx + lean, top_y
        d.line([(bx + lean * 0.6, wy), (fx, fy)], fill=col((96, 110, 70)), width=4)
        anemone_flower(d, rng, fx, fy, rng.uniform(2.6, 3.4), rng.random())
    return img


def frond(d, rng, bx, length, ang, curl, green):
    # A curved rachis with alternating pinnae that shorten toward the tip.
    pts = []
    steps = 40
    for i in range(steps + 1):
        t = i / steps
        a = ang + curl * t * t
        if not pts:
            pts.append((bx, CELL - 6))
        else:
            px, py = pts[-1]
            pts.append((px + math.cos(a) * length / steps, py + math.sin(a) * length / steps))
    for i in range(2, steps - 1):
        t = i / steps
        px, py = pts[i]
        nx, ny = pts[i + 1][0] - px, pts[i + 1][1] - py
        nl = math.hypot(nx, ny) or 1.0
        nx, ny = nx / nl, ny / nl
        pl = length * 0.2 * min(1.0, t * 4.0) * (1.0 - t) ** 0.7
        for side in (1, -1):
            # Pinnae point forward along the frond and droop a little.
            ox, oy = -ny * side, nx * side
            tip = (px + (ox * 0.8 + nx * 0.5) * pl, py + (oy * 0.8 + ny * 0.5) * pl + pl * 0.15)
            w = max(2.0, pl * 0.18)
            g = lerp(green, (120, 170, 70), t * 0.6)
            g = tuple(v * rng.uniform(0.9, 1.08) for v in g)
            d.polygon([(px, py), (px + nx * w + (tip[0] - px) * 0.5, py + ny * w + (tip[1] - py) * 0.5), tip,
                       (px - nx * w + (tip[0] - px) * 0.5, py - ny * w + (tip[1] - py) * 0.5)], fill=col(g))
    for i in range(steps):
        t = i / steps
        d.line([pts[i], pts[i + 1]], fill=col(lerp((70, 80, 40), (110, 150, 60), t)), width=max(2, int(7 * (1 - t))))


def fern(rng):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    fronds = []
    for i in range(11):
        side = (i / 10.0) * 2 - 1
        ang = -math.pi / 2 + side * 0.9 + rng.uniform(-0.12, 0.12)
        fronds.append((abs(side), ang))
    # Outer (lower) fronds first, the upright young ones in front of them.
    fronds.sort(reverse=True)
    for spread, ang in fronds:
        length = CELL * rng.uniform(0.62, 0.8) * (1.0 - spread * 0.25)
        curl = (0.9 + rng.uniform(-0.2, 0.2)) * (1 if ang > -math.pi / 2 else -1) * (0.3 + spread)
        green = (38, 84, 30) if spread > 0.4 else (52, 104, 38)
        frond(d, rng, CELL * 0.5 + rng.uniform(-20, 20), length, ang, curl, green)
    # A young fiddlehead unrolling in the middle.
    cx, cy = CELL * 0.52, CELL * 0.62
    d.line([(CELL * 0.5, CELL - 6), (cx, cy)], fill=col((100, 120, 60)), width=6)
    for k in range(60):
        t = k / 60
        a = t * 4 * math.pi
        r = 26 * (1 - t) + 4
        d.ellipse([cx + math.cos(a) * r - 5, cy - 20 + math.sin(a) * r - 5, cx + math.cos(a) * r + 5, cy - 20 + math.sin(a) * r + 5], fill=col((118, 150, 70)))
    return img


def moss(rng):
    # Seen from above: a soft irregular cushion made of thousands of tiny star-like shoots.
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    shape = Image.new("L", (CELL, CELL), 0)
    sd = ImageDraw.Draw(shape)
    for _ in range(26):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0, CELL * 0.24)
        x, y = CELL * 0.5 + math.cos(a) * rr, CELL * 0.5 + math.sin(a) * rr
        r = rng.uniform(CELL * 0.1, CELL * 0.2)
        sd.ellipse([x - r, y - r, x + r, y + r], fill=255)
    shape = shape.filter(ImageFilter.GaussianBlur(18)).point(lambda v: 255 if v > 110 else int(v * 2.3))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, CELL, CELL], fill=col((52, 74, 26)))
    for _ in range(9000):
        x, y = rng.uniform(0, CELL), rng.uniform(0, CELL)
        c = lerp((66, 100, 30), (150, 182, 66), rng.random() ** 1.2)
        r = rng.uniform(2.0, 5.0)
        for k in range(3):
            a = k * math.pi / 3 + rng.uniform(0, 1)
            d.line([(x - math.cos(a) * r, y - math.sin(a) * r), (x + math.cos(a) * r, y + math.sin(a) * r)], fill=col(c), width=2)
    # A few bright sporophyte heads.
    for _ in range(40):
        x, y = rng.uniform(CELL * 0.25, CELL * 0.75), rng.uniform(CELL * 0.25, CELL * 0.75)
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=col((170, 120, 60)))
    # Darker toward the rim, where the cushion curves down.
    rim = shape.filter(ImageFilter.GaussianBlur(30))
    dark = Image.new("RGBA", (CELL, CELL), (30, 40, 16, 255))
    img = Image.composite(img, dark, rim.point(lambda v: min(255, int(v * 1.6))))
    img.putalpha(shape)
    return img


def mushroom(d, rng, x, base_y, h, cap_r):
    stem_w = cap_r * 0.28
    top = base_y - h
    d.polygon([(x - stem_w, base_y), (x + stem_w, base_y), (x + stem_w * 0.8, top), (x - stem_w * 0.8, top)], fill=col((226, 214, 190)))
    d.line([(x + stem_w * 0.5, base_y), (x + stem_w * 0.45, top)], fill=col((196, 180, 150)), width=4)
    # The gills in shadow under the cap, then the domed cap, lighter at its crown.
    d.ellipse([x - cap_r, top - cap_r * 0.18, x + cap_r, top + cap_r * 0.22], fill=col((120, 90, 60)))
    brown = lerp((132, 82, 42), (170, 112, 60), rng.random())
    d.pieslice([x - cap_r, top - cap_r * 0.9, x + cap_r, top + cap_r * 0.5], 180, 360, fill=col(brown))
    d.ellipse([x - cap_r * 0.45, top - cap_r * 0.72, x + cap_r * 0.25, top - cap_r * 0.35], fill=col(tuple(v * 1.22 for v in brown)))


def mushrooms(rng):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # Leaf litter at the foot: small brown curled leaves and a few moss tufts.
    for _ in range(40):
        x = CELL * 0.5 + rng.gauss(0, CELL * 0.18)
        y = CELL - rng.uniform(8, 70)
        w = rng.uniform(24, 46)
        c = lerp((110, 70, 36), (160, 120, 60), rng.random())
        d.ellipse([x - w, y - w * 0.3, x + w, y + w * 0.3], fill=col(c))
    for _ in range(20):
        bx = CELL * 0.5 + rng.gauss(0, CELL * 0.2)
        blade(d, rng, bx, CELL * rng.uniform(0.05, 0.12), 3.0, (40, 60, 24), (90, 120, 50), rng.uniform(-0.3, 0.3), 0.0)
    xs = [CELL * (0.22 + 0.56 * i / 5) + rng.uniform(-30, 30) for i in range(6)]
    rng.shuffle(xs)
    group = sorted([(rng.uniform(0.12, 0.34), x) for x in xs], key=lambda g: -g[0])
    for h, x in group:
        mushroom(d, rng, x, CELL - rng.uniform(20, 60), CELL * h, CELL * rng.uniform(0.08, 0.12) * (0.7 + h))
    return img


def main():
    rng = random.Random(17)
    mask = edge_mask()
    atlas = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    for cell, make in enumerate([anemones, fern, moss, mushrooms]):
        c = make(rng)
        c.putalpha(ImageChops.multiply(c.getchannel("A"), mask))
        c = c.resize((CELL // 2, CELL // 2), Image.LANCZOS)
        atlas.paste(c, ((cell % 2) * CELL // 2, (cell // 2) * CELL // 2))
    # A dark leaf-green under the transparent pixels, so mipmaps do not fringe.
    under = Image.new("RGB", atlas.size, (58, 80, 34))
    under.paste(atlas.convert("RGB"), mask=atlas.getchannel("A"))
    under.putalpha(atlas.getchannel("A"))
    under.save(OUT)


if __name__ == "__main__":
    main()
