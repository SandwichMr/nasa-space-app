#!/usr/bin/env python3
"""Generates the original, seamless lunar-base interior textures.

Pure Python (zlib only), deterministic: the same seed always produces the same PNGs.
Usage: python3 tools/art-pipeline/generate_lunar_interior_textures.py [output_dir]

Floor tiles are 256 x 256 px and imported at 128 PPU, so one tile repeats every 2 m.
Wall, furniture and edge-shadow sprites are 9-slice sprites (borders set in their .meta).
"""
import math
import os
import random
import struct
import sys
import zlib

DEFAULT_OUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..",
    "Assets", "_Project", "Art", "Sprites", "Environment", "LunarBase", "Interior")


# ---------------------------------------------------------------- image helpers
class Image:
    def __init__(self, w, h, color=(0, 0, 0, 255)):
        self.w, self.h = w, h
        c = [float(v) for v in color]
        self.px = [list(c) for _ in range(w * h)]

    def get(self, x, y):
        return self.px[(y % self.h) * self.w + (x % self.w)]

    def set(self, x, y, c):
        self.px[(y % self.h) * self.w + (x % self.w)] = [float(v) for v in c]

    def blend(self, x, y, c, a):
        """Alpha-blend colour c over pixel (wrapping, so drawing stays seamless)."""
        if a <= 0:
            return
        p = self.get(x, y)
        a = min(1.0, a)
        for i in range(3):
            p[i] = p[i] * (1 - a) + c[i] * a

    def shade(self, x, y, k):
        """Multiply RGB by k (k>1 lightens)."""
        p = self.get(x, y)
        for i in range(3):
            p[i] = p[i] * k

    def save(self, path):
        raw = bytearray()
        for y in range(self.h):
            raw.append(0)
            for x in range(self.w):
                p = self.px[y * self.w + x]
                raw.extend(max(0, min(255, int(round(v)))) for v in p)

        def chunk(tag, data):
            body = tag + data
            return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

        png = b"\x89PNG\r\n\x1a\n"
        png += chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 6, 0, 0, 0))
        png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        png += chunk(b"IEND", b"")
        with open(path, "wb") as f:
            f.write(png)


def hexc(s, a=255):
    s = s.lstrip("#")
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)


def mix(a, b, t):
    return tuple(a[i] * (1 - t) + b[i] * t for i in range(len(a)))


class TileNoise:
    """Periodic value noise: wraps exactly at the texture size, so tiles are seamless."""

    def __init__(self, size, period, seed):
        rnd = random.Random(seed)
        self.size, self.period = size, period
        self.grid = [[rnd.random() for _ in range(period)] for _ in range(period)]

    def __call__(self, x, y):
        fx = x * self.period / self.size
        fy = y * self.period / self.size
        x0, y0 = int(math.floor(fx)), int(math.floor(fy))
        tx, ty = fx - x0, fy - y0
        tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
        p = self.period
        g = self.grid
        a = g[y0 % p][x0 % p] * (1 - tx) + g[y0 % p][(x0 + 1) % p] * tx
        b = g[(y0 + 1) % p][x0 % p] * (1 - tx) + g[(y0 + 1) % p][(x0 + 1) % p] * tx
        return a * (1 - ty) + b * ty


def fbm(size, seed, periods=(4, 8, 16, 32)):
    layers = [TileNoise(size, p, seed + i) for i, p in enumerate(periods)]
    weights = [0.5 ** i for i in range(len(layers))]
    total = sum(weights)
    return lambda x, y: sum(w * n(x, y) for w, n in zip(weights, layers)) / total


def grain(img, seed, amount):
    """Fine per-pixel grain plus soft blotches; keeps surfaces from looking flat."""
    rnd = random.Random(seed)
    soft = fbm(img.w, seed + 100)
    for y in range(img.h):
        for x in range(img.w):
            k = 1 + (rnd.random() - 0.5) * amount + (soft(x, y) - 0.5) * amount * 1.6
            img.shade(x, y, k)


def panel(img, x0, y0, w, h, face, seam, bevel=3, seam_w=2, light=1.16, dark=0.80):
    """Raised plate with a dark seam, light top-left edge and shaded bottom-right edge.
    Unity textures are bottom-up but PNG rows are top-down: 'top' here is the PNG top."""
    for y in range(y0, y0 + h):
        for x in range(x0, x0 + w):
            dx, dy = x - x0, y - y0
            if dx < seam_w or dy < seam_w:
                img.set(x, y, seam)
                continue
            img.set(x, y, face)
            ix, iy = dx - seam_w, dy - seam_w
            iw, ih = w - seam_w, h - seam_w
            if iy < bevel or ix < bevel:
                img.shade(x, y, light)
            elif iy >= ih - bevel or ix >= iw - bevel:
                img.shade(x, y, dark)


def disc(img, cx, cy, r, color, alpha=1.0, soft=1.0):
    for y in range(int(cy - r - 2), int(cy + r + 3)):
        for x in range(int(cx - r - 2), int(cx + r + 3)):
            d = math.hypot(x + .5 - cx, y + .5 - cy)
            a = max(0.0, min(1.0, (r - d) / soft + .5))
            img.blend(x, y, color, a * alpha)


def rivet(img, cx, cy, r=3.2):
    disc(img, cx + .8, cy + .9, r + .6, (0, 0, 0), .35)      # drop shadow
    disc(img, cx, cy, r, (150, 158, 168))
    disc(img, cx - r * .3, cy - r * .3, r * .45, (235, 240, 245), .8)


def rect_fill(img, x0, y0, w, h, color, alpha=1.0):
    for y in range(y0, y0 + h):
        for x in range(x0, x0 + w):
            img.blend(x, y, color, alpha)


S = 256  # floor tile size, 2 m at 128 PPU


# ---------------------------------------------------------------- floors
def floor_corridor():
    """Main corridor and hub: light, warm-grey metal plates (1 m) with rivets."""
    img = Image(S, S)
    tones = [hexc("8f98a3"), hexc("8a939f"), hexc("939ca6"), hexc("8c95a1")]
    for i, (px, py) in enumerate([(0, 0), (128, 0), (0, 128), (128, 128)]):
        panel(img, px, py, 128, 128, tones[i], hexc("454d57"))
    grain(img, 11, .05)
    for px in (0, 128):
        for py in (0, 128):
            for rx, ry in ((12, 12), (116, 12), (12, 116), (116, 116)):
                rivet(img, px + rx, py + ry)
    # faint walking-guide stripe in the centre of each plate row (cyan, base palette)
    for x in range(S):
        for y in (63, 64, 191, 192):
            img.blend(x, y, hexc("6fd0e6"), .10)
    return img


def floor_eva():
    """EVA preparation: dark anti-slip rubber with raised studs and yellow guide marks."""
    img = Image(S, S)
    for px, py in [(0, 0), (128, 0), (0, 128), (128, 128)]:
        panel(img, px, py, 128, 128, hexc("5b626b"), hexc("2a2f35"), light=1.10, dark=.85)
    grain(img, 81, .045)
    for gy in range(8, S, 16):
        for gx in range(8, S, 16):
            ox = 8 if (gy // 16) % 2 else 0
            disc(img, gx + ox + .7, gy + .8, 3.6, (0, 0, 0), .3)
            disc(img, gx + ox, gy, 3.2, hexc("7a828c"))
            disc(img, gx + ox - 1, gy - 1, 1.3, (200, 205, 212), .6)
    # yellow corner brackets on each plate (safety marking)
    yellow = hexc("f2c230")
    for px, py in [(0, 0), (128, 0), (0, 128), (128, 128)]:
        for (cx, cy, sx, sy) in ((px + 8, py + 8, 1, 1), (px + 120, py + 8, -1, 1),
                                 (px + 8, py + 120, 1, -1), (px + 120, py + 120, -1, -1)):
            for i in range(18):
                for w in range(4):
                    img.blend(cx + sx * i, cy + sy * w, yellow, .9)
                    img.blend(cx + sx * w, cy + sy * i, yellow, .9)
    return img


def floor_airlock():
    """Airlock chamber: steel grating over a dark pit, framed by hazard stripes."""
    img = Image(S, S, hexc("1c2228"))
    pitch, bar = 16, 5
    for y in range(S):
        for x in range(S):
            on_x = (x % pitch) < bar
            on_y = (y % pitch) < bar
            if on_x or on_y:
                img.set(x, y, hexc("8d96a1"))
                if (on_x and x % pitch == 0) or (on_y and y % pitch == 0):
                    img.shade(x, y, 1.2)
                elif (on_x and x % pitch == bar - 1) or (on_y and y % pitch == bar - 1):
                    img.shade(x, y, .72)
    grain(img, 91, .05)
    # hazard frame on the tile border (yellow / black diagonal stripes, 12 px wide)
    for y in range(S):
        for x in range(S):
            edge = min(x, y, S - 1 - x, S - 1 - y)
            if edge < 12:
                stripe = ((x + y) // 14) % 2 == 0
                img.set(x, y, hexc("f2c230") if stripe else hexc("23272c"))
                if edge in (0, 11):
                    img.shade(x, y, .7)
    return img


# ---------------------------------------------------------------- 9-slice sprites
def wall_sprite():
    """64 x 64, 16 px borders. Flat centre so stretching along any axis stays clean."""
    n = 64
    img = Image(n, n, hexc("d6dbe0"))
    outline, light, dark = hexc("1b222a"), hexc("f6f8fa"), hexc("9aa4ae")
    for y in range(n):
        for x in range(n):
            e = min(x, y, n - 1 - x, n - 1 - y)
            if e < 3:
                img.set(x, y, outline)
            elif e < 8:
                top_left = (y < n - 1 - y and y <= x and y <= n - 1 - x) or (x < n - 1 - x and x <= y and x <= n - 1 - y)
                img.set(x, y, light if top_left else dark)
            elif e < 10:
                img.set(x, y, hexc("c3c9cf"))
    return img


def furniture_sprite():
    """Benches and storage tops: dark slate with a lit rim."""
    n = 64
    img = Image(n, n, hexc("45535f"))
    for y in range(n):
        for x in range(n):
            e = min(x, y, n - 1 - x, n - 1 - y)
            if e < 3:
                img.set(x, y, hexc("141a20"))
            elif e < 6:
                img.set(x, y, hexc("8da2b2") if y < n / 2 else hexc("5f707e"))
            elif e < 8:
                img.set(x, y, hexc("37434d"))
    return img


def edge_shadow_sprite():
    """Transparent centre, soft dark edges: ambient occlusion where floor meets wall."""
    n = 64
    img = Image(n, n, (0, 0, 0, 0))
    for y in range(n):
        for x in range(n):
            e = min(x, y, n - 1 - x, n - 1 - y) + .5
            a = max(0.0, 1 - e / 24.0) ** 2 * 0.55
            img.set(x, y, (8, 12, 18, a * 255))
    return img


TEXTURES = {
    "floor-corridor": floor_corridor,
    "floor-eva": floor_eva,
    "floor-airlock": floor_airlock,
    "wall-panel": wall_sprite,
    "furniture-top": furniture_sprite,
    "edge-shadow": edge_shadow_sprite,
}


def main():
    out = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT)
    os.makedirs(out, exist_ok=True)
    for name, make in TEXTURES.items():
        path = os.path.join(out, name + ".png")
        make().save(path)
        print("wrote", path)


if __name__ == "__main__":
    main()
