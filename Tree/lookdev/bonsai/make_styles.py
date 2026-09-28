"""Draws the journal's bonsai style pages (design doc section 16 F): five classic styles as ink
sketches with a light wash, on a transparent background, to lie on the journal's paper.
Pillow only, seeded. Run from Tree/: python lookdev/bonsai/make_styles.py
Outputs ui/bonsai_styles/<style>.png (512 x 600).
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

OUT = "ui/bonsai_styles/"
W, H = 512, 600
INK = (52, 41, 31)
WASH = (96, 122, 74)
POT_WASH = (140, 110, 92)


def wobble(rng, pts, amount=1.2):
    return [(x + rng.uniform(-amount, amount), y + rng.uniform(-amount, amount)) for x, y in pts]


def bezier(p0, p1, p2, p3, n=40):
    out = []
    for i in range(n + 1):
        t = i / n
        a = (1 - t) ** 3
        b = 3 * (1 - t) ** 2 * t
        c = 3 * (1 - t) * t * t
        d = t ** 3
        out.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return out


def trunk(draw, rng, pts, w0, w1):
    """A tapering trunk: two ink outlines and a few bark strokes between them."""
    n = len(pts)
    left, right = [], []
    for i in range(n):
        j = min(i + 1, n - 1)
        k = max(i - 1, 0)
        dx = pts[j][0] - pts[k][0]
        dy = pts[j][1] - pts[k][1]
        ln = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / ln, dx / ln
        w = w0 + (w1 - w0) * i / (n - 1)
        left.append((pts[i][0] + nx * w, pts[i][1] + ny * w))
        right.append((pts[i][0] - nx * w, pts[i][1] - ny * w))
    draw.polygon(left + right[::-1], fill=(120, 92, 70, 70))
    draw.line(wobble(rng, left, 0.8), fill=INK + (255,), width=3)
    draw.line(wobble(rng, right, 0.8), fill=INK + (255,), width=3)
    for i in range(2, n - 2, 3):
        a = left[i]
        b = right[i]
        m = ((a[0] * 2 + b[0]) / 3, (a[1] * 2 + b[1]) / 3)
        draw.line([m, (m[0] + rng.uniform(-3, 3), m[1] + rng.uniform(6, 12))], fill=INK + (150,), width=1)


def branch(draw, rng, a, b, w):
    draw.line(wobble(rng, [a, ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2 - 4), b], 0.6), fill=INK + (255,), width=w, joint="curve")


def pad(draw, rng, cx, cy, rx, ry):
    """A foliage pad: a soft wash, then a cloud outline of small scalloped arcs, and hatching."""
    draw.ellipse([cx - rx, cy - ry, cx + rx, cy + ry * 0.9], fill=WASH + (95,))
    k = int(10 + rx / 5)
    for i in range(k):
        a = math.pi + math.pi * i / (k - 1)
        x = cx + math.cos(a) * rx * 0.92
        y = cy + math.sin(a) * ry * 0.95
        r = rx * rng.uniform(0.16, 0.24)
        draw.arc([x - r, y - r, x + r, y + r], 180, 360, fill=INK + (235,), width=2)
    # The flat underside and a few strokes of shade.
    draw.line(wobble(rng, [(cx - rx * 0.95, cy + ry * 0.05), (cx, cy + ry * 0.35), (cx + rx * 0.95, cy + ry * 0.05)], 1.0), fill=INK + (200,), width=2)
    for i in range(int(rx / 7)):
        x = cx - rx * 0.7 + i * 7 + rng.uniform(-2, 2)
        draw.line([(x, cy + ry * 0.05), (x - 5, cy + ry * 0.25)], fill=INK + (90,), width=1)


def pot(draw, rng, cx, top, w, h, tall=False):
    if tall:
        pts = [(cx - w * 0.42, top), (cx + w * 0.42, top), (cx + w * 0.36, top + h), (cx - w * 0.36, top + h)]
    else:
        pts = [(cx - w * 0.5, top), (cx + w * 0.5, top), (cx + w * 0.44, top + h), (cx - w * 0.44, top + h)]
    draw.polygon(pts, fill=POT_WASH + (80,))
    draw.line(wobble(rng, pts + [pts[0]], 0.7), fill=INK + (255,), width=3)
    draw.line(wobble(rng, [(pts[0][0] - 4, top + 6), (pts[1][0] + 4, top + 6)], 0.6), fill=INK + (200,), width=2)
    for fx in (-0.34, 0.34):
        x = cx + w * fx
        draw.rectangle([x - 12, top + h, x + 12, top + h + 7], outline=INK + (220,), width=2)


def formal(draw, rng):
    pot(draw, rng, 256, 470, 300, 70)
    pts = [(256 + rng.uniform(-1, 1), 470 - i * 38) for i in range(10)]
    trunk(draw, rng, pts, 24, 5)
    for i, (y, side, rx) in enumerate([(360, -1, 95), (318, 1, 88), (268, -1, 76), (228, 1, 66), (186, -1, 54), (150, 1, 44)]):
        tip = (256 + side * rx * 0.85, y + 6)
        branch(draw, rng, (256, y + 18), tip, 5 - i // 2)
        pad(draw, rng, 256 + side * rx * 0.6, y, rx * 0.75, 22)
    pad(draw, rng, 256, 118, 42, 28)


def informal(draw, rng):
    pot(draw, rng, 256, 470, 300, 70)
    pts = bezier((250, 470), (170, 360), (340, 280), (240, 130))
    trunk(draw, rng, pts, 26, 5)
    for (tx, ty, px, py, rx) in [(215, 395, 120, 350, 85), (300, 330, 380, 300, 80), (262, 250, 170, 225, 72), (275, 190, 350, 170, 60)]:
        branch(draw, rng, (tx, ty), (px + (20 if px < tx else -20), py + 12), 4)
        pad(draw, rng, px, py, rx, 24)
    pad(draw, rng, 245, 118, 52, 32)


def slanting(draw, rng):
    pot(draw, rng, 256, 470, 300, 70)
    pts = bezier((215, 470), (250, 380), (320, 260), (370, 150))
    trunk(draw, rng, pts, 26, 6)
    # A strong root on the side it leans away from.
    draw.line(wobble(rng, [(200, 468), (160, 470)], 0.5), fill=INK + (255,), width=4)
    for (tx, ty, px, py, rx) in [(248, 385, 150, 330, 78), (290, 320, 395, 300, 70), (330, 245, 250, 215, 62)]:
        branch(draw, rng, (tx, ty), (px + (18 if px < tx else -18), py + 10), 4)
        pad(draw, rng, px, py, rx, 22)
    pad(draw, rng, 380, 128, 58, 32)


def cascade(draw, rng):
    pot(draw, rng, 200, 250, 150, 170, tall=True)
    pts = bezier((200, 250), (210, 160), (330, 160), (360, 290)) + bezier((360, 290), (380, 380), (400, 450), (395, 540))[1:]
    trunk(draw, rng, pts, 20, 4)
    for (px, py, rx) in [(300, 150, 60), (400, 300, 50), (330, 390, 52), (430, 440, 42), (385, 560, 44)]:
        pad(draw, rng, px, py, rx, 20)


def broom(draw, rng):
    pot(draw, rng, 256, 470, 280, 55)
    pts = [(256, 470 - i * 28) for i in range(7)]
    trunk(draw, rng, pts, 22, 12)
    top = (256, 302)
    for i in range(9):
        a = math.radians(-70 + i * 17.5)
        end = (256 + math.sin(a) * 150, 302 - math.cos(a) * 150)
        mid = (256 + math.sin(a) * 70, 302 - math.cos(a) * 80)
        branch(draw, rng, top, mid, 5)
        branch(draw, rng, mid, end, 2)
    draw.ellipse([80, 110, 432, 330], fill=WASH + (70,))
    for i in range(22):
        a = math.pi + math.pi * i / 21
        x = 256 + math.cos(a) * 172
        y = 250 + math.sin(a) * 135
        r = rng.uniform(16, 24)
        draw.arc([x - r, y - r, x + r, y + r], 180, 360, fill=INK + (230,), width=2)
    draw.line(wobble(rng, [(86, 252), (256, 300), (426, 252)], 1.0), fill=INK + (190,), width=2)


STYLES = {"formal_upright": formal, "informal_upright": informal, "slanting": slanting, "cascade": cascade, "broom": broom}


def main():
    os.makedirs(OUT, exist_ok=True)
    for k, (name, fn) in enumerate(STYLES.items()):
        rng = random.Random(700 + k)
        img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        draw = ImageDraw.Draw(img, "RGBA")
        fn(draw, rng)
        img = img.filter(ImageFilter.SMOOTH)
        img.save(OUT + name + ".png")


if __name__ == "__main__":
    main()
