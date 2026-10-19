"""Renders the Cascade app icon and splash logo: a small tumble of clay pebbles in the K: Pebbles palette.

Pure numpy + Pillow, no Unity needed. Run:

    python tools/icon/make_icon.py

Writes the Unity icon PNGs and splash-logo.png to unity/Assets/Cascade/Art/Icon/ and cascade.ico next to this
script. Every output is derived from one pebble layer, so the Windows icon, the Android
adaptive icon (foreground + background layers) and the legacy/round icons always match.
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_PNG = os.path.join(HERE, "..", "..", "unity", "Assets", "Cascade", "Art", "Icon")
OUT_ICO = os.path.join(HERE, "cascade.ico")
FONTS = os.path.join(HERE, "..", "..", "unity", "Assets", "Cascade", "UI", "Fonts")

DESIGN = 1024          # design canvas (the pebble layer is drawn at this size)
SS = 2                 # supersampling factor
N = DESIGN * SS

# K: Pebbles palette (docs/art/STYLE-K-Pebbles.md)
SAND, SAGE, CLAY, SLATE, CHARCOAL = "#cdb48d", "#8fa08a", "#b98468", "#7b8794", "#4f4a46"
BG_TOP, BG_BOT = "#f1eadf", "#d9cdb8"

rng = np.random.default_rng(7)
Y, X = np.mgrid[0:N, 0:N].astype(np.float32)


def rgb(hex_):
    h = hex_.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32) / 255.0


def local(cx, cy, deg):
    """Canvas coords -> pebble-local coords (rotated by deg about the centre)."""
    a = math.radians(deg)
    dx, dy = X - cx * SS, Y - cy * SS
    return dx * math.cos(a) + dy * math.sin(a), -dx * math.sin(a) + dy * math.cos(a)


def sdf_ellipse(px, py, a, b):
    a, b = a * SS, b * SS
    k0 = np.sqrt((px / a) ** 2 + (py / b) ** 2)
    k1 = np.sqrt((px / (a * a)) ** 2 + (py / (b * b)) ** 2)
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-6)


def sdf_round_box(px, py, hx, hy, r):
    hx, hy, r = hx * SS, hy * SS, r * SS
    qx, qy = np.abs(px) - (hx - r), np.abs(py) - (hy - r)
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r


def sdf_leaf(px, py, half_len, half_w, r):
    """Vesica (two-arc lens) along x with rounded tips."""
    l, w, r = (half_len - r) * SS, (half_w - r) * SS, r * SS
    rc = (l * l + w * w) / (2 * w)
    off = rc - w
    d1 = np.hypot(px, py - off) - rc
    d2 = np.hypot(px, py + off) - rc
    return np.maximum(d1, d2) - r


def sdf_capsule(px, py, half_len, rad):
    half_len, rad = half_len * SS, rad * SS
    qx = np.abs(px) - (half_len - rad)
    return np.hypot(np.maximum(qx, 0), py) - rad


def _box(a, w, axis):
    """Moving average of odd width w along axis (edge-padded), in float32."""
    pad = w // 2
    widths = [(0, 0)] * a.ndim
    widths[axis] = (pad + 1, pad)
    c = np.cumsum(np.pad(a, widths, mode="edge"), axis=axis, dtype=np.float64)
    hi = np.take(c, range(w, c.shape[axis]), axis=axis)
    lo = np.take(c, range(0, c.shape[axis] - w), axis=axis)
    return ((hi - lo) / w).astype(np.float32)


def blur(a, radius):
    """Gaussian blur approximated by three box passes; float throughout, so no banding."""
    sigma = max(radius * SS, 0.5)
    w = int(math.sqrt(12 * sigma * sigma / 3 + 1)) | 1
    out = a.astype(np.float32)
    for _ in range(3):
        out = _box(_box(out, w, 0), w, 1)
    return out


def pebble(canvas, sdf, base, rim, flecks=3, tilt=(0.0, 0.0), shadow=1.0, seed=0):
    """Shade one pebble onto canvas (HxWx4 premultiplied-free float RGBA)."""
    r = np.random.default_rng(seed)
    alpha = np.clip(0.5 - sdf, 0, 1)
    inside = alpha > 0

    # soft contact shadow, then a tighter dark one under the lip
    mask = (sdf < 0).astype(np.float32)
    for dx, dy, rad, k in ((16, 26, 26, 0.26), (6, 10, 9, 0.30)):
        sh = np.roll(np.roll(blur(mask, rad), int(dy * SS), 0), int(dx * SS), 1)
        a = sh * k * shadow
        shadow_col = rgb("#5e4c3a")
        canvas[..., :3] = canvas[..., :3] * (1 - a[..., None]) + shadow_col * a[..., None]
        canvas[..., 3] = canvas[..., 3] + a * (1 - canvas[..., 3])

    # height field: the mask blurred to a soft dome (an SDF dome would crease along the medial axis)
    d = np.clip(-sdf, 0, None)
    h = blur(mask, rim * 0.42) * rim * SS * 2.2
    gy, gx = np.gradient(h)
    nx, ny, nz = -gx + tilt[0], -gy + tilt[1], np.ones_like(h)
    inv = 1 / np.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny, nz = nx * inv, ny * inv, nz * inv

    light = np.array([-0.45, -0.62, 0.64], np.float32)
    light /= np.linalg.norm(light)
    dif = np.clip(nx * light[0] + ny * light[1] + nz * light[2], 0, 1)
    half = light + np.array([0, 0, 1], np.float32)
    half /= np.linalg.norm(half)
    spec = np.clip(nx * half[0] + ny * half[1] + nz * half[2], 0, 1) ** 9

    col = base[None, None, :] * (0.68 + 0.40 * dif[..., None])
    col = col + rgb("#fff1d8") * (0.07 * spec)[..., None]
    # worn edge: slightly lighter where the pebble turns away, darker right at the lip
    lip = np.clip(1 - d / (5 * SS), 0, 1)[..., None]
    col = col * (1 - 0.18 * lip)

    # flecks: 2-3 small speckles per pebble, like the in-game clay
    ys, xs = np.nonzero(d > rim * SS * 0.8)
    for _ in range(flecks):
        i = r.integers(len(ys))
        fy, fx = ys[i], xs[i]
        fr = r.uniform(4.0, 7.0) * SS
        blob = np.clip(1 - np.hypot(X - fx, Y - fy) / fr, 0, 1) ** 0.6
        dark = r.random() < 0.7
        tone = base * 0.72 if dark else np.minimum(base * 1.25 + 0.06, 1)
        col = col * (1 - 0.55 * blob[..., None]) + tone * 0.55 * blob[..., None] * (0.68 + 0.40 * dif[..., None])

    col = col + (rng.normal(0, 0.012, h.shape)[..., None]).astype(np.float32)  # clay grain

    a = alpha * inside
    canvas[..., :3] = canvas[..., :3] * (1 - a[..., None]) + np.clip(col, 0, 1) * a[..., None]
    canvas[..., 3] = canvas[..., 3] + a * (1 - canvas[..., 3])


def pebble_layer():
    """The tumble of pebbles on transparent, designed to sit inside a ~680px circle."""
    cv = np.zeros((N, N, 4), np.float32)
    # Back to front, a loose pinwheel: sand top-left, clay right, sage bottom-left, slate
    # bottom-right, and a small charcoal pebble still dropping in at the top right.
    pebble(cv, sdf_ellipse(*local(392, 372, -16), 172, 128), rgb(SAND), rim=120, seed=1)
    pebble(cv, sdf_round_box(*local(636, 508, 11), 128, 124, 60), rgb(CLAY), rim=118, seed=2)
    pebble(cv, sdf_capsule(*local(664, 758, -26), 118, 60), rgb(SLATE), rim=60, seed=3)
    pebble(cv, sdf_leaf(*local(396, 668, -22), 190, 100, 22), rgb(SAGE), rim=96, seed=4)
    pebble(cv, sdf_ellipse(*local(742, 272, 0), 66, 66), rgb(CHARCOAL), rim=66, seed=5, shadow=0.85, flecks=2)
    return cv


def to_image(cv, size):
    img = Image.fromarray((np.clip(cv, 0, 1) * 255).astype(np.uint8), "RGBA")
    return img.resize((size, size), Image.LANCZOS)


def background(size):
    """Pale sand: a vertical gradient with a soft central glow and a little grain."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32) / (size - 1)
    top, bot = rgb(BG_TOP), rgb(BG_BOT)
    col = top * (1 - yy[..., None]) + bot * yy[..., None]
    glow = np.clip(1 - np.hypot(xx - 0.42, yy - 0.34) / 0.75, 0, 1) ** 1.6
    col = col + glow[..., None] * 0.05
    col = col + rng.normal(0, 0.008, (size, size, 1)).astype(np.float32)
    return Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8), "RGB").convert("RGBA")


def rounded_mask(size, radius_frac):
    s = size * 4
    m = Image.new("L", (s, s), 0)
    from PIL import ImageDraw
    ImageDraw.Draw(m).rounded_rectangle((0, 0, s - 1, s - 1), radius=int(s * radius_frac), fill=255)
    return m.resize((size, size), Image.LANCZOS)


def circle_mask(size):
    s = size * 4
    m = Image.new("L", (s, s), 0)
    from PIL import ImageDraw
    ImageDraw.Draw(m).ellipse((0, 0, s - 1, s - 1), fill=255)
    return m.resize((size, size), Image.LANCZOS)


def compose(fg_full, size, scale, mask):
    """Background + pebbles scaled about the centre, clipped by mask (None = full bleed)."""
    base = background(size)
    fg = fg_full.resize((size, size), Image.LANCZOS)
    if scale != 1:
        inner = int(round(size * scale))
        fg = fg.resize((inner, inner), Image.LANCZOS)
        layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        layer.paste(fg, ((size - inner) // 2, (size - inner) // 2))
        fg = layer
    base.alpha_composite(fg)
    if mask is not None:
        base.putalpha(mask.resize((size, size), Image.LANCZOS))
    return base


def splash_logo(fg_full):
    """Transparent logo for Unity's splash: the pebbles over the wordmark and tagline (title-screen type)."""
    w = 1280
    logo = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    box = fg_full.getbbox()
    pebbles = fg_full.crop(box)
    k = 780 / pebbles.width
    pebbles = pebbles.resize((round(pebbles.width * k), round(pebbles.height * k)), Image.LANCZOS)
    logo.alpha_composite(pebbles, ((w - pebbles.width) // 2, 70))

    d = ImageDraw.Draw(logo)
    title = ImageFont.truetype(os.path.join(FONTS, "DeliusSwashCaps-Regular.ttf"), 250)
    tag = ImageFont.truetype(os.path.join(FONTS, "Delius-Regular.ttf"), 64)
    clay, slate = tuple(int(v * 255) for v in rgb(CLAY)), tuple(int(v * 255) for v in rgb(SLATE))
    d.text((w // 2, 930), "Cascade", font=title, fill=clay, anchor="mm")
    d.text((w // 2, 1120), "match, connect, relax", font=tag, fill=slate, anchor="mm")
    return logo


def main():
    out_png = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.abspath(OUT_PNG)
    out_ico = os.path.join(out_png, "cascade.ico") if len(sys.argv) > 1 else os.path.abspath(OUT_ICO)
    os.makedirs(out_png, exist_ok=True)

    cv = pebble_layer()
    fg_full = to_image(cv, DESIGN)

    # Desktop / Windows / store art: rounded square, pebbles filling it
    master = compose(fg_full, 1024, 1.22, rounded_mask(1024, 0.2237))
    master.save(os.path.join(out_png, "icon-1024.png"))
    master.resize((256, 256), Image.LANCZOS).save(out_ico, sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    master.resize((256, 256), Image.LANCZOS).save(os.path.join(out_png, "icon-256.png"))

    # Android adaptive: 432px layers (108dp @ 4x); the pebbles stay inside the 66dp safe circle
    a_scale = 0.80
    fg = fg_full.resize((432, 432), Image.LANCZOS)
    inner = int(round(432 * a_scale))
    layer = Image.new("RGBA", (432, 432), (0, 0, 0, 0))
    layer.paste(fg.resize((inner, inner), Image.LANCZOS), ((432 - inner) // 2, (432 - inner) // 2))
    layer.save(os.path.join(out_png, "icon-adaptive-fg-432.png"))
    background(432).convert("RGB").save(os.path.join(out_png, "icon-adaptive-bg-432.png"))

    # Android legacy (rounded square) and round, 192px
    compose(fg_full, 192, 1.12, rounded_mask(192, 0.18)).save(os.path.join(out_png, "icon-legacy-192.png"))
    compose(fg_full, 192, 1.0, circle_mask(192)).save(os.path.join(out_png, "icon-round-192.png"))
    splash_logo(fg_full).save(os.path.join(out_png, "splash-logo.png"))
    print("wrote icons to", out_png, "and", out_ico)


if __name__ == "__main__":
    main()
