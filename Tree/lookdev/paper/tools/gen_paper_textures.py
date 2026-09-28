#!/usr/bin/env python3
"""Generates the paper look-dev textures (all procedural, seeded, tileable).

Run from the project folder:  python3 lookdev/paper/tools/gen_paper_textures.py
Needs numpy, scipy and Pillow. Output goes to lookdev/paper/textures/.

crumple_normal.png  1024 tileable: RGB = tangent-space normal of a crumpled sheet
                    (three layers of straight mountain/valley crease segments plus soft waviness),
                    A = signed crease sharpness (0.5 = flat, <0.5 valley, >0.5 ridge).
paper_detail.png    512 tileable: R = fibre grain, G = foxing spots, B = age mottling,
                    A = ink pressure / fine tooth noise.
leather_normal.png  512 tileable: RGB = normal of pebbled leather, A = grain height.
pressed_leaf.png    256: a pressed, dried linden leaf cut from the CC0 LeafSet004 scan.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "textures")
PROJECT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
rng = np.random.default_rng(20260927)


def fft_noise(n, beta, seed):
    """Periodic (tileable) 1/f^beta noise, normalised to 0..1."""
    r = np.random.default_rng(seed)
    white = r.standard_normal((n, n))
    fx = np.fft.fftfreq(n)[:, None]
    fy = np.fft.fftfreq(n)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    spec = np.fft.fft2(white) / f ** beta
    spec[0, 0] = 0
    out = np.real(np.fft.ifft2(spec))
    return (out - out.min()) / (out.max() - out.min())


def fft_blur(a, sigma):
    return ndimage.gaussian_filter(a, sigma, mode="wrap")


def voronoi_cones(n, count, seed, tilt, grid_scale=1.0):
    """min over cells of (cone + random tilted plane): continuous, facetted, sharp ridges on borders."""
    r = np.random.default_rng(seed)
    pts = r.random((count, 2)) * n
    grads = r.normal(0, tilt, (count, 2))
    # Keep every cone rising away from its seed (slope < 1), so a far cell never wins
    # across the wrap seam and the texture stays seamless.
    mag = np.linalg.norm(grads, axis=1, keepdims=True)
    grads = grads * np.minimum(1.0, 0.75 / np.maximum(mag, 1e-6))
    offs = r.normal(0, 0.15, count) * n / np.sqrt(count)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    h = np.full((n, n), np.inf, np.float32)
    for (px, py), g, o in zip(pts, grads, offs):
        dx = (xx - px + n / 2) % n - n / 2
        dy = (yy - py + n / 2) % n - n / 2
        cone = np.sqrt(dx * dx + dy * dy) * grid_scale + dx * g[0] + dy * g[1] + o
        h = np.minimum(h, cone)
    return h


def normal_from_height(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    nx = -gx * strength
    ny = -gy * strength
    nz = np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return nx / l, ny / l, nz / l


def to8(a):
    return np.clip(a * 255.0 + 0.5, 0, 255).astype(np.uint8)


def crease_field(n, count, seed, length, width, amp):
    """Straight crease segments (random mountain or valley folds): each adds a tent that is
    sharp on its crest and planar on its flanks, faded out towards the segment's ends."""
    r = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    h = np.zeros((n, n), np.float32)
    for _ in range(count):
        px, py = r.random(2) * n
        ang = r.random() * np.pi
        ln = r.uniform(0.5, 1.0) * length
        wd = r.uniform(0.5, 1.0) * width
        dx = (xx - px + n / 2) % n - n / 2
        dy = (yy - py + n / 2) % n - n / 2
        t = dx * np.cos(ang) + dy * np.sin(ang)
        d = -dx * np.sin(ang) + dy * np.cos(ang)
        along = np.clip((ln - np.abs(t)) / (0.35 * ln), 0.0, 1.0)
        along = along * along * (3 - 2 * along)
        tent = np.maximum(0.0, wd - np.abs(d))
        h += (1 if r.random() < 0.5 else -1) * amp * r.uniform(0.4, 1.0) * along * tent
    return h


def crumple():
    n = 1024
    h = crease_field(n, 70, 1, 420.0, 150.0, 0.25)     # long folds, broad facets
    h += crease_field(n, 170, 2, 170.0, 55.0, 0.3)     # the crumple network
    h += crease_field(n, 650, 3, 60.0, 12.0, 0.3)     # small crinkles
    h += (fft_noise(n, 2.4, 4) - 0.5) * 60.0           # soft overall waviness
    h = fft_blur(h, 0.7)
    nx, ny, nz = normal_from_height(h, 1.0)
    lap = ndimage.laplace(fft_blur(h, 0.6), mode="wrap")
    crease = np.clip(0.5 + lap * 1.2, 0.0, 1.0)
    img = np.dstack([to8(nx * 0.5 + 0.5), to8(ny * 0.5 + 0.5), to8(nz * 0.5 + 0.5), to8(crease)])
    Image.fromarray(img, "RGBA").save(os.path.join(OUT, "crumple_normal.png"), optimize=True)


def fibres(n, count, seed):
    """Short curved fibre strands on a wrapping canvas."""
    r = np.random.default_rng(seed)
    big = Image.new("L", (n * 3, n * 3), 0)
    d = ImageDraw.Draw(big)
    for _ in range(count):
        x, y = r.random(2) * n + n
        ang = r.random() * np.pi
        length = r.uniform(4, 22)
        pts = []
        for s in range(8):
            ang += r.normal(0, 0.18)
            x += np.cos(ang) * length / 8
            y += np.sin(ang) * length / 8
            pts.append((x, y))
        v = int(r.uniform(90, 255))
        for ox in (-n, 0, n):
            for oy in (-n, 0, n):
                d.line([(p[0] + ox, p[1] + oy) for p in pts], fill=v, width=1)
    a = np.asarray(big, np.float32)[n:2 * n, n:2 * n] / 255.0
    return fft_blur(a, 0.5)


def foxing(n, seed):
    """Rust-brown age spots: clustered, each a dark core with a soft halo."""
    r = np.random.default_rng(seed)
    cluster = fft_noise(n, 2.2, seed + 1)
    out = np.zeros((n, n), np.float32)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    placed = 0
    while placed < 170:
        x, y = r.random(2) * n
        if r.random() > cluster[int(y), int(x)] ** 2 * 1.4:
            continue
        placed += 1
        rad = r.choice([0.8, 1.2, 1.8, 2.5, 4.0, 7.0], p=[0.3, 0.25, 0.2, 0.13, 0.08, 0.04])
        dx = (xx - x + n / 2) % n - n / 2
        dy = (yy - y + n / 2) % n - n / 2
        d = np.sqrt(dx * dx + dy * dy)
        core = np.clip(1.0 - d / rad, 0, 1) ** 0.7
        halo = np.exp(-(d / (rad * 3.0)) ** 2) * 0.35
        out = np.maximum(out, (core + halo) * r.uniform(0.4, 1.0))
    wobble = fft_noise(n, 1.0, seed + 2)
    return np.clip(out * (0.6 + 0.8 * wobble), 0, 1)


def detail():
    n = 512
    grain = fft_noise(n, 0.9, 10) * 0.6 + fibres(n, 1600, 11) * 0.6
    grain = (grain - grain.min()) / (grain.max() - grain.min())
    fox = foxing(n, 12)
    mottle = fft_noise(n, 2.0, 13) * 0.7 + fft_noise(n, 1.3, 14) * 0.3
    mottle = (mottle - mottle.min()) / (mottle.max() - mottle.min())
    tooth = fft_noise(n, 0.6, 15)
    img = np.dstack([to8(grain), to8(fox), to8(mottle), to8(tooth)])
    Image.fromarray(img, "RGBA").save(os.path.join(OUT, "paper_detail.png"), optimize=True)


def leather():
    from scipy.spatial import cKDTree
    n = 512
    r = np.random.default_rng(20)
    pts = r.random((1800, 2)) * n
    tiled = np.concatenate([pts + np.array([ox, oy]) for ox in (-n, 0, n) for oy in (-n, 0, n)])
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    dist, _ = cKDTree(tiled).query(np.stack([xx.ravel(), yy.ravel()], 1), k=2)
    seam = (dist[:, 1] - dist[:, 0]).reshape(n, n)
    h = np.sqrt(np.minimum(seam, 6.0))                   # rounded pebbles, sharp seams
    h += (fft_noise(n, 1.8, 21) - 0.5) * 3.0             # hide-scale undulation
    h = fft_blur(h, 0.7)
    nx, ny, nz = normal_from_height(h, 0.9)
    hn = (h - h.min()) / (h.max() - h.min())
    img = np.dstack([to8(nx * 0.5 + 0.5), to8(ny * 0.5 + 0.5), to8(nz * 0.5 + 0.5), to8(hn)])
    Image.fromarray(img, "RGBA").save(os.path.join(OUT, "leather_normal.png"), optimize=True)


def pressed_leaf():
    base = os.path.join(PROJECT, "assets", "leaves")
    col = Image.open(os.path.join(base, "LeafSet004_1K-JPG_Color.jpg")).convert("RGB")
    op = Image.open(os.path.join(base, "LeafSet004_1K-JPG_Opacity.jpg")).convert("L")
    box = (100, 30, 360, 510)  # the top-left leaf
    c = np.asarray(col.crop(box), np.float32) / 255.0
    a = np.asarray(op.crop(box), np.float32) / 255.0
    # Keep only the leaf blob (drop the white guide lines in the opacity scan).
    lab, cnt = ndimage.label(a > 0.5)
    sizes = ndimage.sum(np.ones_like(a), lab, range(1, cnt + 1))
    keep = lab == (int(np.argmax(sizes)) + 1)
    a = a * ndimage.binary_dilation(keep, iterations=2)
    # Dried: green -> olive/brown, lower saturation, darker veins kept, blotchy browning.
    lum = c @ np.array([0.3, 0.59, 0.11])
    dry = np.dstack([lum * 1.25 + 0.12, lum * 1.05 + 0.08, lum * 0.55 + 0.02])
    blot = fft_noise(512, 2.0, 30)[: c.shape[0], : c.shape[1]]
    brown = np.dstack([lum * 0.95 + 0.1, lum * 0.62 + 0.04, lum * 0.3])
    t = np.clip((blot - 0.35) * 2.0, 0, 1)[..., None]
    rgb = dry * (1 - t) + brown * t
    rgb = rgb * 0.6 + c * np.array([0.5, 0.45, 0.3]) * 0.4
    img = np.dstack([to8(np.clip(rgb, 0, 1)), to8(a)])
    im = Image.fromarray(img, "RGBA")
    im.thumbnail((256, 256), Image.LANCZOS)
    im.save(os.path.join(OUT, "pressed_leaf.png"), optimize=True)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    crumple()
    detail()
    leather()
    pressed_leaf()
    print("written to", os.path.abspath(OUT))
