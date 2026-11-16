#!/usr/bin/env python3
"""Generates original, function-specific sprites for every interactive station of the lunar base.

Pure Python (zlib only), deterministic. Shapes are drawn with signed-distance functions so the
edges are anti-aliased. Style: 3/4 top-down equipment on a navy platform whose rim colour encodes the
function (water blue, air green, energy yellow, seals cyan, shelter amber, science purple, logistics orange),
readable for 7-12 year olds.
No painted text: captions stay runtime text.

Usage: python3 tools/art-pipeline/generate_station_sprites.py [output_dir]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from generate_lunar_interior_textures import Image, hexc, mix  # noqa: E402

DEFAULT_OUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..",
    "Assets", "_Project", "Art", "Sprites", "Environment", "LunarBase", "Stations")

N = 256
OUT = hexc("17202a")          # outline
SW = 4.0                      # outline width (px)

WHITE, WHITE_SH = hexc("eef2f5"), hexc("c3ccd6")
METAL, METAL_DK = hexc("9aa6b2"), hexc("66727e")
PANEL, PANEL_MID, SCREEN = hexc("2b3742"), hexc("3d4c5a"), hexc("13202a")
GOLD, GOLD_DK = hexc("ffd36b"), hexc("e0902f")
CYAN, ORANGE, GREEN, YELLOW = hexc("4fc3dc"), hexc("f28c38"), hexc("5fd08a"), hexc("f2c230")
BLUE, BLUE_DK, PURPLE, RED = hexc("4aa8ec"), hexc("2a6fb8"), hexc("a68ce3"), hexc("e05a4f")


# ---------------------------------------------------------------- signed distance functions
def clamp01(v):
    return 0.0 if v < 0 else 1.0 if v > 1 else v


def sd_box(cx, cy, hw, hh, r=0.0):
    def f(x, y):
        qx, qy = abs(x - cx) - hw + r, abs(y - cy) - hh + r
        return math.hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - r
    return f, (cx - hw, cy - hh, cx + hw, cy + hh)


def sd_circle(cx, cy, r):
    return (lambda x, y: math.hypot(x - cx, y - cy) - r), (cx - r, cy - r, cx + r, cy + r)


def sd_ellipse(cx, cy, rx, ry):
    m = min(rx, ry)
    return (lambda x, y: (math.hypot((x - cx) / rx, (y - cy) / ry) - 1) * m), (cx - rx, cy - ry, cx + rx, cy + ry)


def sd_capsule(ax, ay, bx, by, r):
    def f(x, y):
        px, py, vx, vy = x - ax, y - ay, bx - ax, by - ay
        h = clamp01((px * vx + py * vy) / (vx * vx + vy * vy or 1))
        return math.hypot(px - vx * h, py - vy * h) - r
    return f, (min(ax, bx) - r, min(ay, by) - r, max(ax, bx) + r, max(ay, by) + r)


def sd_poly(pts):
    def f(x, y):
        d = float("inf")
        inside = False
        n = len(pts)
        for i in range(n):
            ax, ay = pts[i]
            bx, by = pts[(i + 1) % n]
            vx, vy, px, py = bx - ax, by - ay, x - ax, y - ay
            h = clamp01((px * vx + py * vy) / (vx * vx + vy * vy or 1))
            d = min(d, math.hypot(px - vx * h, py - vy * h))
            if (ay > y) != (by > y) and x < ax + (y - ay) * (bx - ax) / (by - ay):
                inside = not inside
        return -d if inside else d
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    return f, (min(xs), min(ys), max(xs), max(ys))


def sd_ring_arc(cx, cy, r, w, a0, a1):
    """Arc of a ring between angles a0..a1 (radians, PNG y-down, 0 = +x, clockwise)."""
    def f(x, y):
        a = math.atan2(y - cy, x - cx) % (2 * math.pi)
        lo, hi = a0 % (2 * math.pi), a1 % (2 * math.pi)
        within = lo <= a <= hi if lo <= hi else (a >= lo or a <= hi)
        if within:
            return abs(math.hypot(x - cx, y - cy) - r) - w
        ends = [(cx + r * math.cos(t), cy + r * math.sin(t)) for t in (a0, a1)]
        return min(math.hypot(x - ex, y - ey) for ex, ey in ends) - w
    return f, (cx - r - w, cy - r - w, cx + r + w, cy + r + w)


def vgrad(top, bottom, y0, y1):
    """Vertical gradient as a colour function."""
    return lambda x, y: mix(top, bottom, clamp01((y - y0) / max(1.0, y1 - y0)))


def draw(img, shape, fill=None, outline=True, sw=SW, alpha=1.0, stroke=OUT):
    f, (x0, y0, x1, y1) = shape
    pad = sw + 2 if outline else 2
    for y in range(max(0, int(y0 - pad)), min(img.h, int(y1 + pad) + 1)):
        for x in range(max(0, int(x0 - pad)), min(img.w, int(x1 + pad) + 1)):
            d = f(x + .5, y + .5)
            if outline:
                # with a fill the stroke sits outside the shape; without one it is a centred ring
                ca = clamp01(.5 - (d - sw)) if fill is not None else clamp01(.5 - (abs(d) - sw / 2))
                if ca > 0:
                    over(img, x, y, stroke, ca * alpha)
            if fill is not None:
                ca = clamp01(.5 - d)
                if ca > 0:
                    c = fill(x + .5, y + .5) if callable(fill) else fill
                    over(img, x, y, c, ca * alpha * (c[3] / 255.0 if len(c) > 3 else 1))


def over(img, x, y, c, a):
    """Premultiplied 'over' onto an RGBA canvas that starts transparent."""
    p = img.px[y * img.w + x]
    da = p[3] / 255.0
    oa = a + da * (1 - a)
    if oa <= 0:
        return
    for i in range(3):
        p[i] = (c[i] * a + p[i] * da * (1 - a)) / oa
    p[3] = oa * 255


def canvas():
    return Image(N, N, (0, 0, 0, 0))


def floor_shadow(img, cx=128, cy=236, rx=96, ry=13):
    draw(img, sd_ellipse(cx, cy, rx, ry), (0, 0, 0, 255), outline=False, alpha=.28)


def light(img, x, y, color, r=6):
    draw(img, sd_circle(x, y, r), color, sw=2.5)
    draw(img, sd_circle(x - r * .3, y - r * .3, r * .35), (255, 255, 255), outline=False, alpha=.7)


def screw_panel(img, cx, cy, hw, hh, r, top, bottom):
    draw(img, sd_box(cx, cy, hw, hh, r), vgrad(top, bottom, cy - hh, cy + hh))
    draw(img, sd_box(cx, cy - hh + 5, hw - r, 2.5, 2), (255, 255, 255), outline=False, alpha=.18)


# ---------------------------------------------------------------- 3/4 machine kit
# Style reference: top-down 3/4 equipment standing on a navy platform with a coloured rim, glass
# cylinders with metal caps, grey pipes with collars, small glowing LEDs and a hard drop shadow
# towards the lower right. Rim colour encodes the function so children can match machine and resource.
EDGE = hexc("0d1220")
NAVY_A, NAVY_B, NAVY_FRONT = hexc("2e3c60"), hexc("1f2946"), hexc("151c31")
STEEL_A, STEEL_B = hexc("d3dbe6"), hexc("7d8899")
LED_TEAL = hexc("4ff5e6")
RIM = {
    "water": hexc("3f7dff"), "air": hexc("3fd08a"), "energy": hexc("f2c230"), "seal": hexc("3fd6e6"),
    "shelter": hexc("f29a38"), "science": hexc("a77bff"), "logistics": hexc("f2a33a"),
}


def shade(c, k):
    return tuple(min(255.0, v * k) for v in c[:3]) + ((c[3],) if len(c) > 3 else (255,))


def cyl_shade(base, cx, r):
    """Horizontal shading of a vertical cylinder: dark edges, bright highlight left of centre."""
    def f(x, y):
        t = max(-1.0, min(1.0, (x - cx) / r))
        k = .58 + .42 * math.cos(t * math.pi / 2) + .32 * math.exp(-((t + .45) / .2) ** 2)
        return shade(base, k)
    return f


def union(*shapes):
    fs = [s[0] for s in shapes]
    bb = (min(s[1][0] for s in shapes), min(s[1][1] for s in shapes),
          max(s[1][2] for s in shapes), max(s[1][3] for s in shapes))
    return (lambda x, y: min(f(x, y) for f in fs)), bb


def drop_shadow(img, shape, dx=10, dy=10, alpha=.38):
    f, (x0, y0, x1, y1) = shape
    draw(img, ((lambda x, y: f(x - dx, y - dy)), (x0 + dx, y0 + dy, x1 + dx, y1 + dy)),
         (0, 0, 0, 255), outline=False, alpha=alpha)


def platform(img, rim, cx=128, top=124, bottom=200, hw=110, depth=16, r=18):
    whole = sd_box(cx, (top + bottom + depth) / 2, hw, (bottom + depth - top) / 2, r)
    drop_shadow(img, whole)
    draw(img, whole, NAVY_FRONT, sw=3, stroke=EDGE)
    draw(img, sd_box(cx, (top + bottom) / 2, hw, (bottom - top) / 2, r), vgrad(NAVY_A, NAVY_B, top, bottom), outline=False)
    draw(img, sd_box(cx, (top + bottom) / 2, hw - 7, (bottom - top) / 2 - 7, r - 5), None, sw=3.2, stroke=rim)
    draw(img, sd_box(cx, (top + bottom) / 2 + 3, hw - 20, (bottom - top) / 2 - 17, r - 9), None, sw=1.6, stroke=hexc("111830"))
    draw(img, sd_box(cx, bottom + depth / 2 + 1, hw - 16, 1.6, 1), rim, outline=False, alpha=.55)


def led(img, x, y, color=LED_TEAL, w=7, h=4):
    draw(img, sd_box(x, y, w + 5, h + 5, 5), color, outline=False, alpha=.22)
    draw(img, sd_box(x, y, w + 3, h + 3, 3), hexc("1a2236"), sw=2, stroke=EDGE)
    draw(img, sd_box(x, y, w, h, 2), color, outline=False)
    draw(img, sd_box(x - w * .3, y - h * .3, w * .4, h * .3, 1), (255, 255, 255), outline=False, alpha=.6)


def pipe(img, pts, r=7, collars=True):
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        draw(img, sd_capsule(ax, ay, bx, by, r), STEEL_B, sw=3, stroke=EDGE)
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        draw(img, sd_capsule(ax, ay, bx, by, r - 2), hexc("a9b4c3"), outline=False)
        ox, oy = (0, -r * .35) if abs(by - ay) < abs(bx - ax) else (-r * .35, 0)
        draw(img, sd_capsule(ax + ox, ay + oy, bx + ox, by + oy, r * .28), (255, 255, 255), outline=False, alpha=.55)
    if collars:
        for (x, y) in pts[1:-1]:
            draw(img, sd_box(x, y, r + 3, r + 3, 3), vgrad(STEEL_A, STEEL_B, y - r, y + r), sw=2.5, stroke=EDGE)


def cylinder(img, cx, top, bottom, r, glass=hexc("3c4c62"), liquid=None, level=.6, solid=None,
             led_color=LED_TEAL, cap=True):
    """3/4 cylinder: glass (optionally with liquid) or a solid body, metal top cap and LED base ring."""
    ry = r * .34
    body = union(sd_box(cx, (top + bottom) / 2, r, (bottom - top) / 2, 0), sd_ellipse(cx, bottom, r, ry))
    ly = bottom - (bottom - top) * level
    if solid is not None:
        draw(img, body, cyl_shade(solid, cx, r), sw=3, stroke=EDGE)
    else:
        g, l = cyl_shade(glass, cx, r), cyl_shade(liquid, cx, r) if liquid else None
        draw(img, body, (lambda x, y: l(x, y) if (l and y >= ly) else g(x, y)), sw=3, stroke=EDGE)
        if liquid:
            draw(img, sd_ellipse(cx, ly, r - 1, ry), shade(liquid, 1.35), outline=False, alpha=.9)
            draw(img, sd_ring_arc(cx, ly, r - 4, 1.2, math.pi * 1.05, math.pi * 1.95), (255, 255, 255), outline=False, alpha=.35)
        draw(img, sd_capsule(cx - r * .55, top + 14, cx - r * .55, bottom - 18, r * .09 + 1), (255, 255, 255), outline=False, alpha=.45)
        draw(img, sd_capsule(cx - r * .33, top + 18, cx - r * .33, bottom - 30, r * .04 + .6), (255, 255, 255), outline=False, alpha=.3)
    # base ring with LED
    ring = union(sd_box(cx, bottom - 3, r + 4, 9, 0), sd_ellipse(cx, bottom + 6, r + 4, ry + 1))
    draw(img, ring, cyl_shade(hexc("4a5770"), cx, r + 4), sw=3, stroke=EDGE)
    if led_color:
        led(img, cx, bottom + 6, led_color, 6, 3)
    if cap:
        band = union(sd_box(cx, top + 2, r + 3, 9, 0), sd_ellipse(cx, top + 11, r + 3, ry + 1))
        draw(img, band, cyl_shade(hexc("c5cfdc"), cx, r + 3), sw=3, stroke=EDGE)
        draw(img, sd_ellipse(cx, top - 7, r + 3, ry + 1), vgrad(hexc("eef2f7"), hexc("aab5c4"), top - 7 - ry, top - 7 + ry), sw=3, stroke=EDGE)
        draw(img, sd_ellipse(cx, top - 7, r * .6, ry * .6), hexc("2a3550"), sw=2, stroke=EDGE)


def box3(img, cx, top, w, h, d, top_color, front_color, edge=EDGE, sw=3):
    """3/4 box: lighter top face of depth d, then the front face of height h."""
    draw(img, sd_box(cx, top + (d + h) / 2, w / 2, (d + h) / 2, 4), front_color if not callable(front_color) else front_color, sw=sw, stroke=edge)
    draw(img, sd_box(cx, top + d / 2, w / 2 - 1.5, d / 2 - 1.5, 3), top_color, outline=False)
    draw(img, sd_box(cx, top + d, w / 2 - 2, 1, 0), edge, outline=False, alpha=.5)


def screen(img, cx, cy, hw, hh, glow, content=None):
    draw(img, sd_box(cx, cy, hw + 5, hh + 5, 6), vgrad(hexc("3a4763"), hexc("1d2640"), cy - hh, cy + hh), sw=3, stroke=EDGE)
    draw(img, sd_box(cx, cy, hw, hh, 3), vgrad(hexc("0b1424"), hexc("152238"), cy - hh, cy + hh), outline=False)
    draw(img, sd_box(cx, cy, hw, hh, 3), glow, outline=False, alpha=.12)
    if content:
        content()


def front_hatch(img, cx, cy, hw=34, hh=13, rim=None):
    draw(img, sd_box(cx, cy, hw + 4, hh + 4, 5), rim or hexc("3f7dff"), sw=2.5, stroke=EDGE)
    draw(img, sd_box(cx, cy, hw, hh, 4), vgrad(hexc("e3e8ef"), hexc("aab4c2"), cy - hh, cy + hh), sw=2, stroke=EDGE)
    draw(img, sd_poly([(cx - hw * .45, cy + hh), (cx - hw * .3, cy + hh * .35), (cx + hw * .3, cy + hh * .35), (cx + hw * .45, cy + hh)]),
         hexc("8d98a8"), outline=False)


def lightning(img, cx, cy, s, color):
    pts = [(10, -22), (-12, 4), (2, 4), (-8, 24), (14, -4), (0, -4), (8, -22)]
    draw(img, sd_poly([(cx + x * s, cy + y * s) for x, y in pts]), color, sw=2, stroke=EDGE)


def checkmark(img, cx, cy, s, color):
    draw(img, sd_capsule(cx - 9 * s, cy, cx - 2 * s, cy + 7 * s, 2.6 * s), color, outline=False)
    draw(img, sd_capsule(cx - 2 * s, cy + 7 * s, cx + 11 * s, cy - 8 * s, 2.6 * s), color, outline=False)


# ---------------------------------------------------------------- stations
def water_recycler():
    img = canvas()
    rim = RIM["water"]
    platform(img, rim)
    pipe(img, [(70, 52), (70, 30), (128, 30), (186, 30), (186, 52)])
    pipe(img, [(128, 30), (128, 76)], collars=False)
    for y in (100, 148):
        pipe(img, [(100, y), (112, y)], r=6, collars=False)
        pipe(img, [(144, y), (156, y)], r=6, collars=False)
    cylinder(img, 70, 64, 160, 30, liquid=hexc("6f857f"), level=.62)        # used water
    cylinder(img, 186, 64, 160, 30, liquid=hexc("2f6bdc"), level=.82)       # clean water
    cylinder(img, 128, 86, 162, 16, solid=hexc("58647a"), cap=True)          # filter column
    draw(img, sd_box(128, 124, 8, 18, 4), hexc("5fb4ff"), sw=2.5, stroke=EDGE)
    draw(img, sd_box(128, 124, 12, 22, 6), hexc("5fb4ff"), outline=False, alpha=.25)
    draw(img, sd_box(126, 118, 2, 8, 1), (255, 255, 255), outline=False, alpha=.7)
    front_hatch(img, 120, 196, rim=rim)
    draw(img, sd_box(196, 200, 16, 13, 4), vgrad(hexc("4a5770"), hexc("2b354c"), 187, 213), sw=2.5, stroke=EDGE)
    led(img, 196, 198, LED_TEAL, 7, 5)
    return img


def air_scrubber():
    img = canvas()
    rim = RIM["air"]
    platform(img, rim)
    pipe(img, [(140, 92), (172, 92)], r=6, collars=False)
    pipe(img, [(140, 140), (172, 140)], r=6, collars=False)
    box3(img, 88, 52, 104, 112, 18, hexc("a9b6c6"), vgrad(hexc("8794a7"), hexc("566275"), 70, 182))
    draw(img, sd_circle(88, 124, 42), vgrad(hexc("1a2d2a"), hexc("0c1716"), 82, 166), sw=3.5, stroke=EDGE)
    for k in range(5):                                                                        # fan blades
        a = k * 2 * math.pi / 5
        pts = [(88 + rr * math.cos(a + u), 124 + rr * math.sin(a + u))
               for rr, u in ((10, 0), (36, .25), (38, .62), (12, .75))]
        draw(img, sd_poly(pts), vgrad(hexc("d8f6ec"), hexc("7fcdb3"), 86, 162), sw=2, stroke=EDGE)
    draw(img, sd_circle(88, 124, 10), vgrad(STEEL_A, STEEL_B, 114, 134), sw=2.5, stroke=EDGE)
    draw(img, sd_ring_arc(88, 124, 36, 1.4, 0, 2 * math.pi - .01), rim, outline=False, alpha=.8)
    for i in range(4):                                                                        # top vent slots
        draw(img, sd_capsule(56 + i * 22, 61, 66 + i * 22, 61, 2.2), hexc("4c586b"), outline=False)
    # CO2 absorber canisters (white granules inside)
    for cx in (184, 222):
        cylinder(img, cx, 72, 162, 15, glass=hexc("55667c"), liquid=hexc("e9edf2"), level=.72, led_color=hexc("6dff9e"))
        for j in range(6):
            draw(img, sd_circle(cx - 6 + (j % 3) * 6, 128 + (j // 3) * 12, 1.8), hexc("aab4bf"), outline=False)
    led(img, 88, 176, hexc("6dff9e"), 9, 4)
    draw(img, sd_box(136, 200, 18, 11, 4), vgrad(hexc("4a5770"), hexc("2b354c"), 189, 211), sw=2.5, stroke=EDGE)
    led(img, 136, 199, hexc("6dff9e"), 8, 4)
    return img


def suit_charger():
    img = canvas()
    rim = RIM["energy"]
    platform(img, rim)
    pipe(img, [(66, 54), (66, 36), (120, 36), (120, 54)], r=6)
    for cx, lvl in ((66, .78), (120, .46)):                                                    # battery cells
        cylinder(img, cx, 66, 160, 22, glass=hexc("3a4658"), liquid=hexc("f2c230"), level=lvl, led_color=hexc("ffe066"))
        for j in range(1, 4):
            y = 160 - 94 * j / 4
            draw(img, sd_box(cx, y, 20, 1, 0), EDGE, outline=False, alpha=.35)
    box3(img, 186, 50, 54, 112, 14, hexc("b9c3d1"), vgrad(hexc("6a778b"), hexc("3f4a5e"), 64, 176))  # charging pylon
    screen(img, 186, 98, 17, 22, hexc("f2c230"), lambda: lightning(img, 186, 98, .85, hexc("ffd84a")))
    led(img, 186, 140, hexc("ffe066"), 9, 4)
    draw(img, sd_capsule(208, 150, 222, 176, 5.5), hexc("2b3346"), sw=2.5, stroke=EDGE)         # cable + plug
    draw(img, sd_capsule(222, 176, 214, 202, 5.5), hexc("2b3346"), sw=2.5, stroke=EDGE)
    draw(img, sd_box(206, 206, 11, 8, 3), vgrad(STEEL_A, STEEL_B, 198, 214), sw=2.5, stroke=EDGE)
    front_hatch(img, 96, 196, hw=30, rim=rim)
    return img


def consumables_panel():
    img = canvas()
    rim = RIM["seal"]
    platform(img, rim)
    pipe(img, [(76, 50), (76, 32), (128, 32), (180, 32), (180, 50)])
    pipe(img, [(128, 32), (128, 84)], collars=False)
    cylinder(img, 76, 62, 160, 30, glass=hexc("5f8a7a"), led_color=hexc("8fffc4"))           # oxygen (gas)
    for bx, by, br in ((68, 136, 7), (86, 116, 5), (78, 96, 4), (64, 108, 3), (88, 144, 3)):
        draw(img, sd_circle(bx, by, br), (230, 255, 245, 140), sw=1.6, stroke=hexc("dffff0"))
    cylinder(img, 180, 62, 160, 30, liquid=hexc("2f6bdc"), level=.7)                          # water
    cylinder(img, 128, 94, 160, 13, solid=hexc("5a667c"), led_color=None)                     # fill valve
    draw(img, sd_circle(128, 122, 11), vgrad(hexc("f3f6fa"), hexc("c3ccd8"), 111, 133), sw=2.5, stroke=EDGE)
    draw(img, sd_capsule(128, 122, 134, 115, 1.6), hexc("e05a4f"), outline=False)
    draw(img, sd_capsule(140, 152, 152, 176, 4.5), hexc("2b3346"), sw=2.5, stroke=EDGE)        # hose + nozzle
    draw(img, sd_capsule(152, 176, 146, 196, 4.5), hexc("2b3346"), sw=2.5, stroke=EDGE)
    draw(img, sd_box(140, 202, 12, 7, 3), vgrad(STEEL_A, STEEL_B, 195, 209), sw=2.5, stroke=EDGE)
    led(img, 92, 198, hexc("8fffc4"), 8, 4)
    led(img, 196, 198, hexc("5fb4ff"), 8, 4)
    return img


def suit_check():
    img = canvas()
    rim = RIM["seal"]
    platform(img, rim)
    # glass test chamber with a helmet inside and a glowing scanning ring around the neck seal
    cylinder(img, 106, 56, 162, 46, glass=hexc("3d5068"), led_color=LED_TEAL)
    draw(img, sd_box(106, 146, 16, 12, 3), vgrad(STEEL_A, STEEL_B, 134, 158), sw=2.5, stroke=EDGE)
    draw(img, sd_circle(106, 108, 30), vgrad(hexc("f1f5f8"), hexc("b9c3cf"), 78, 138), sw=3, stroke=EDGE)
    draw(img, sd_ellipse(106, 110, 21, 15), vgrad(hexc("ffd36b"), hexc("e0902f"), 95, 125), sw=2.5, stroke=EDGE)
    draw(img, sd_ellipse(99, 104, 6, 3.5), (255, 255, 255), outline=False, alpha=.75)
    draw(img, sd_box(106, 138, 26, 5, 2.5), hexc("4fdcec"), sw=2, stroke=EDGE)                # seal ring
    draw(img, sd_ring_arc(106, 138, 44, 2.2, math.pi * .02, math.pi * .98), LED_TEAL, outline=False, alpha=.9)
    draw(img, sd_ellipse(106, 138, 50, 9), LED_TEAL, outline=False, alpha=.12)
    draw(img, sd_capsule(78, 70, 78, 140, 3), (255, 255, 255), outline=False, alpha=.35)
    box3(img, 196, 70, 46, 96, 12, hexc("b9c3d1"), vgrad(hexc("6a778b"), hexc("3f4a5e"), 82, 178))
    screen(img, 196, 108, 14, 16, hexc("6dff9e"), lambda: checkmark(img, 196, 108, 1.1, hexc("6dff9e")))
    led(img, 196, 146, LED_TEAL, 8, 4)
    front_hatch(img, 106, 198, hw=28, rim=rim)
    return img


def shelter_panel():
    img = canvas()
    rim = RIM["shelter"]
    platform(img, rim)
    amber = hexc("ffb347")
    draw(img, sd_poly([(108, 152), (148, 152), (176, 60), (80, 60)]), amber, outline=False, alpha=.12)  # beam
    shield = [(128, 32), (172, 46), (168, 96), (128, 134), (88, 96), (84, 46)]
    draw(img, sd_poly(shield), amber, outline=False, alpha=.18)
    draw(img, sd_poly(shield), None, sw=3, stroke=amber)
    for i in range(3):
        y = 100 - i * 20
        draw(img, sd_capsule(110, y + 10, 128, y - 3, 3.6), hexc("ffe2b0"), outline=False, alpha=.95)
        draw(img, sd_capsule(128, y - 3, 146, y + 10, 3.6), hexc("ffe2b0"), outline=False, alpha=.95)
    cylinder(img, 128, 150, 172, 26, solid=hexc("58647a"), cap=False, led_color=amber)       # emitter
    draw(img, sd_ellipse(128, 150, 26, 9), vgrad(hexc("ffe2b0"), amber, 141, 159), sw=3, stroke=EDGE)
    for cx, colors in ((52, (hexc("6dff9e"), amber)), (204, (amber, hexc("ff6b5e")))):          # control pillars
        box3(img, cx, 96, 34, 76, 10, hexc("b9c3d1"), vgrad(hexc("6a778b"), hexc("3f4a5e"), 106, 182))
        led(img, cx, 126, colors[0], 7, 5)
        led(img, cx, 148, colors[1], 7, 5)
    return img


def science_lab():
    img = canvas()
    rim = RIM["science"]
    platform(img, rim)
    # microscope
    draw(img, sd_box(78, 160, 30, 9, 4), vgrad(hexc("5d6a80"), hexc("39445a"), 151, 169), sw=3, stroke=EDGE)
    draw(img, sd_capsule(96, 156, 104, 92, 9), vgrad(STEEL_A, STEEL_B, 92, 156), sw=3, stroke=EDGE)
    draw(img, sd_box(74, 132, 22, 4, 2), vgrad(STEEL_A, STEEL_B, 128, 136), sw=2.5, stroke=EDGE)
    draw(img, sd_capsule(68, 116, 102, 62, 12), vgrad(hexc("cdb8ff"), hexc("7d5ad8"), 56, 122), sw=3, stroke=EDGE)
    draw(img, sd_capsule(100, 64, 110, 46, 7), vgrad(STEEL_A, STEEL_B, 44, 66), sw=2.5, stroke=EDGE)
    draw(img, sd_capsule(70, 114, 68, 126, 6), STEEL_B, sw=2.5, stroke=EDGE)
    draw(img, sd_capsule(76, 98, 94, 72, 2.5), (255, 255, 255), outline=False, alpha=.5)
    # sample tubes in a rack
    for i, c in enumerate((hexc("a77bff"), hexc("6dff9e"), hexc("5fb4ff"))):
        cylinder(img, 138 + i * 22, 104, 160, 8, glass=hexc("4a5a70"), liquid=c, level=.55 + .1 * i, led_color=None)
    draw(img, sd_box(160, 150, 38, 5, 2), vgrad(STEEL_A, STEEL_B, 145, 155), sw=2.5, stroke=EDGE)
    # flask
    flask = [(206, 92), (218, 92), (218, 114), (236, 160), (188, 160), (206, 114)]
    draw(img, sd_poly(flask), (190, 210, 230, 150), sw=3, stroke=EDGE)
    draw(img, sd_poly([(199, 134), (225, 134), (236, 160), (188, 160)]), hexc("b48cf0"), outline=False)
    draw(img, sd_circle(206, 148, 3), (255, 255, 255), outline=False, alpha=.8)
    draw(img, sd_circle(216, 142, 2), (255, 255, 255), outline=False, alpha=.8)
    led(img, 110, 198, hexc("c9a8ff"), 8, 4)
    led(img, 150, 198, LED_TEAL, 8, 4)
    return img


def sample_analyzer():
    img = canvas()
    rim = RIM["science"]
    platform(img, rim)
    pipe(img, [(122, 96), (146, 96)], r=6, collars=False)
    cylinder(img, 84, 60, 160, 36, glass=hexc("3a4a62"), led_color=hexc("c9a8ff"))             # analysis chamber
    draw(img, sd_box(84, 146, 18, 8, 3), vgrad(STEEL_A, STEEL_B, 138, 154), sw=2.5, stroke=EDGE)
    draw(img, sd_poly([(64, 136), (70, 116), (86, 108), (102, 118), (102, 136), (84, 142)]),
         vgrad(hexc("c2b6a4"), hexc("7d7264"), 108, 142), sw=2.5, stroke=EDGE)              # lunar rock
    draw(img, sd_circle(80, 122, 3), hexc("5f564a"), outline=False)
    draw(img, sd_poly([(70, 76), (98, 76), (106, 132), (62, 132)]), hexc("c9a8ff"), outline=False, alpha=.18)  # scan light
    box3(img, 182, 56, 76, 110, 14, hexc("d6dde7"), vgrad(hexc("aab5c4"), hexc("6f7b8e"), 70, 180))
    def spectrum():
        pts = [(160, 104), (168, 100), (174, 84), (180, 100), (188, 94), (194, 78), (200, 98), (206, 92)]
        for (ax, ay), (bx, by) in zip(pts, pts[1:]):
            draw(img, sd_capsule(ax, ay, bx, by, 1.8), hexc("c9a8ff"), outline=False)
    screen(img, 182, 94, 26, 18, hexc("c9a8ff"), spectrum)
    draw(img, sd_box(182, 140, 26, 8, 3), hexc("2b3346"), sw=2.5, stroke=EDGE)                # sample drawer
    draw(img, sd_capsule(170, 140, 194, 140, 2), STEEL_A, outline=False)
    led(img, 166, 160, hexc("c9a8ff"), 6, 3)
    led(img, 198, 160, LED_TEAL, 6, 3)
    front_hatch(img, 92, 197, hw=26, rim=rim)
    return img


def logistics_console():
    img = canvas()
    rim = RIM["logistics"]
    platform(img, rim)
    wood_top, wood_front = hexc("f0c07a"), vgrad(hexc("d79a52"), hexc("a96f31"), 100, 190)

    def crate(cx, top, w, h):
        box3(img, cx, top, w, h, 16, wood_top, wood_front)
        draw(img, sd_box(cx, top + 16 + h / 2, w / 2 - 7, h / 2 - 7, 2), None, sw=2.2, stroke=hexc("7a4d22"))
        draw(img, sd_capsule(cx - w / 2 + 9, top + 16 + h - 8, cx + w / 2 - 9, top + 24, 2.4), hexc("8a5a2a"), outline=False)
    crate(78, 112, 80, 60)
    crate(84, 64, 56, 40)
    draw(img, sd_box(84, 92, 9, 7, 2), hexc("4ff5e6"), sw=2, stroke=EDGE)                    # label tag
    box3(img, 186, 56, 58, 112, 14, hexc("b9c3d1"), vgrad(hexc("6a778b"), hexc("3f4a5e"), 70, 182))
    def rows():
        for i in range(4):
            w = 16 if i % 2 == 0 else 11
            draw(img, sd_box(180 + (w - 16) / 2, 88 + i * 9, w, 2.2, 1), hexc("ffd36b") if i == 0 else hexc("8fd3e6"), outline=False)
    screen(img, 186, 100, 20, 22, hexc("ffd36b"), rows)
    draw(img, sd_box(186, 144, 18, 6, 2), hexc("2b3346"), sw=2, stroke=EDGE)                  # scanner slot
    draw(img, sd_capsule(174, 144, 198, 144, 1.4), hexc("ff6b5e"), outline=False)
    led(img, 186, 160, hexc("ffd36b"), 7, 3)
    return img


def suit_figure(img, accent):
    """Spacesuit on a hanger frame (drawn on a 256 canvas, then scaled onto a platform)."""
    for x in (44, 212):
        draw(img, sd_capsule(x, 34, x, 222, 7), cyl_shade(hexc("b4bfcd"), x, 7))
    draw(img, sd_capsule(44, 34, 212, 34, 7), vgrad(STEEL_A, STEEL_B, 27, 41))
    draw(img, sd_capsule(128, 34, 128, 54, 3.5), STEEL_B)
    suit = vgrad(WHITE, WHITE_SH, 50, 225)
    draw(img, sd_box(128, 116, 48, 42, 12), vgrad(hexc("b9c3cd"), hexc("8e99a5"), 74, 158))
    for x0, x1 in ((112, 106), (144, 150)):
        draw(img, sd_capsule(x0, 156, x1, 204, 14), suit)
    for x in (106, 150):
        draw(img, sd_box(x, 211, 18, 9, 6), vgrad(hexc("7d8894"), hexc("57626e"), 202, 220))
    draw(img, sd_capsule(86, 100, 72, 158, 13), suit)
    draw(img, sd_capsule(170, 100, 184, 158, 13), suit)
    draw(img, sd_circle(71, 166, 12), vgrad(hexc("c9d1da"), hexc("98a3ae"), 154, 178))
    draw(img, sd_circle(185, 166, 12), vgrad(hexc("c9d1da"), hexc("98a3ae"), 154, 178))
    draw(img, sd_box(128, 122, 42, 44, 18), suit)
    draw(img, sd_box(128, 152, 42, 6, 3), accent, sw=2.5)
    draw(img, sd_circle(92, 96, 9), accent, sw=2.5)
    draw(img, sd_circle(164, 96, 9), accent, sw=2.5)
    draw(img, sd_box(128, 120, 17, 12, 3), hexc("1d2640"), sw=2.5)
    led(img, 120, 120, hexc("6dff9e"), 3, 2.5)
    led(img, 136, 120, accent, 3, 2.5)
    draw(img, sd_circle(128, 66, 28), vgrad(WHITE, WHITE_SH, 38, 94))
    draw(img, sd_ellipse(128, 69, 20, 15), vgrad(GOLD, GOLD_DK, 54, 84), sw=2.5)
    draw(img, sd_ellipse(121, 63, 7, 4), (255, 255, 255), outline=False, alpha=.75)


def paste_scaled(dst, src, s, ox, oy):
    """Bilinear, alpha-correct paste of src scaled by s with its top-left at (ox, oy)."""
    for y in range(int(oy), min(dst.h, int(oy + src.h * s) + 1)):
        for x in range(int(ox), min(dst.w, int(ox + src.w * s) + 1)):
            fx, fy = (x + .5 - ox) / s - .5, (y + .5 - oy) / s - .5
            x0, y0 = int(math.floor(fx)), int(math.floor(fy))
            tx, ty = fx - x0, fy - y0
            acc, aa = [0.0, 0.0, 0.0], 0.0
            for xx, yy, w in ((x0, y0, (1 - tx) * (1 - ty)), (x0 + 1, y0, tx * (1 - ty)),
                              (x0, y0 + 1, (1 - tx) * ty), (x0 + 1, y0 + 1, tx * ty)):
                if 0 <= xx < src.w and 0 <= yy < src.h:
                    p = src.px[yy * src.w + xx]
                    a = p[3] / 255.0 * w
                    aa += a
                    for i in range(3):
                        acc[i] += p[i] * a
            if aa > 0 and 0 <= x < dst.w:
                over(dst, x, y, tuple(c / aa for c in acc), min(1.0, aa))


def suit_rack(accent, rim):
    def make():
        img = canvas()
        platform(img, rim, top=150, bottom=206, hw=96, depth=14)
        fig = canvas()
        suit_figure(fig, accent)
        paste_scaled(img, fig, .74, 128 - 128 * .74, 30)
        led(img, 128, 212, accent, 9, 3)
        return img
    return make


def regolith_site():
    img = canvas()
    draw(img, sd_ellipse(128, 196, 112, 44), vgrad(hexc("a7a39b"), hexc("7a766f"), 152, 240), sw=3)
    for pts in ([(44, 196), (60, 176), (82, 184), (86, 204), (62, 212)],
                [(170, 208), (184, 190), (206, 194), (212, 214), (190, 222)],
                [(112, 222), (124, 210), (140, 214), (138, 230), (118, 232)]):
        draw(img, sd_poly(pts), vgrad(hexc("b9b3a8"), hexc("7d776d"), 176, 232), sw=3)
    # sample flag (purple = science)
    draw(img, sd_capsule(196, 60, 196, 186, 3.5), METAL_DK)
    draw(img, sd_poly([(199, 60), (242, 74), (199, 90)]), vgrad(hexc("c7b5f5"), PURPLE, 60, 90), sw=3)
    # scoop
    draw(img, sd_capsule(70, 88, 118, 168, 6), vgrad(METAL, METAL_DK, 88, 168))
    draw(img, sd_ellipse(124, 176, 22, 14), vgrad(hexc("d4dbe2"), METAL_DK, 162, 190))
    draw(img, sd_ellipse(124, 172, 14, 7), hexc("8a857c"), outline=False)
    # sample container
    draw(img, sd_box(152, 136, 22, 26, 6), (225, 240, 250, 220), sw=3)
    draw(img, sd_box(152, 112, 24, 6, 3), PURPLE, sw=3)
    draw(img, sd_box(152, 148, 16, 12, 3), hexc("9a948a"), outline=False)
    return img


def crater_core_drill():
    img = canvas()
    draw(img, sd_ellipse(128, 206, 110, 36), vgrad(hexc("a7a39b"), hexc("74706a"), 170, 242), sw=3)
    draw(img, sd_ellipse(128, 206, 40, 12), hexc("4f4c47"), outline=False)                       # bore hole
    for ax, bx in ((128, 52), (128, 204), (128, 128)):                                           # tripod legs
        draw(img, sd_capsule(ax, 70, bx, 206 if bx != 128 else 214, 5), vgrad(METAL, METAL_DK, 70, 214))
    draw(img, sd_capsule(128, 110, 128, 206, 5), hexc("f2b544"))                                 # drill string
    draw(img, sd_box(128, 92, 30, 24, 7), vgrad(hexc("ffbf73"), ORANGE, 68, 116))                # motor
    draw(img, sd_box(128, 92, 14, 10, 3), SCREEN, sw=2.5)
    light(img, 128, 92, GREEN, 4)
    # optional star badge
    star = []
    for k in range(10):
        r = 26 if k % 2 == 0 else 11
        a = -math.pi / 2 + k * math.pi / 5
        star.append((204 + r * math.cos(a), 48 + r * math.sin(a)))
    draw(img, sd_poly(star), vgrad(hexc("ffe27a"), hexc("f2a33a"), 22, 74))
    return img


# name -> (factory, pixels per unit chosen so the world size matches the slot it replaces)
STATIONS = {
    "suit-rack-a": (suit_rack(CYAN, CYAN), 100),
    "suit-rack-b": (suit_rack(ORANGE, ORANGE), 100),
    "suit-charger": (suit_charger, 100),
    "consumables-panel": (consumables_panel, 100),
    "suit-check": (suit_check, 100),
    "shelter-panel": (shelter_panel, 100),
    "science-lab": (science_lab, 100),
    "sample-analyzer": (sample_analyzer, 100),
    "water-recycler": (water_recycler, 256),
    "air-scrubber": (air_scrubber, 256),
    "logistics-console": (logistics_console, 256),
    "regolith-site": (regolith_site, 128),
    "crater-rim": (crater_core_drill, 128),
}


def main():
    out = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT)
    os.makedirs(out, exist_ok=True)
    for name, (make, _ppu) in STATIONS.items():
        path = os.path.join(out, name + ".png")
        make().save(path)
        print("wrote", path)


if __name__ == "__main__":
    main()
