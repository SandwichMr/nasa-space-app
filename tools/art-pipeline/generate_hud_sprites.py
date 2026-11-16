#!/usr/bin/env python3
"""Generates the original pixel-art HUD kit of the lunar base dashboard (phase 4).

Pure Python (zlib only), deterministic. Every asset is authored on a small pixel grid and enlarged with
nearest-neighbour, so it stays crisp when imported with Point filtering. Style guide: navy panels with
chamfered blue pixel borders, glossy pill bars, and bold status symbols readable by 7-12 year olds.
No painted text: every caption stays runtime text.

Usage: python3 tools/art-pipeline/generate_hud_sprites.py [output_dir]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from generate_lunar_interior_textures import Image  # noqa: E402

DEFAULT_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                           "Assets", "_Project", "Art", "Icons", "LunarHud")

T = (0, 0, 0, 0)
OUTLINE = (4, 8, 18, 255)
NAVY = (11, 21, 44, 238)
DEEP = (6, 12, 27, 244)
BLUE, BLUE_LT, BLUE_DK = (47, 109, 240, 255), (120, 170, 255, 255), (22, 46, 112, 255)
STEEL, STEEL_LT = (78, 104, 156, 255), (130, 160, 214, 255)
CYAN, CYAN_LT, CYAN_DK = (54, 214, 238, 255), (170, 245, 255, 255), (14, 120, 150, 255)
GREEN, GREEN_LT, GREEN_DK = (64, 206, 86, 255), (170, 245, 160, 255), (20, 120, 40, 255)
AMBER, AMBER_LT, AMBER_DK = (255, 172, 36, 255), (255, 222, 130, 255), (170, 96, 10, 255)
RED, RED_LT, RED_DK = (232, 52, 52, 255), (255, 150, 140, 255), (140, 18, 24, 255)
TEAL, TEAL_LT = (30, 176, 150, 255), (140, 236, 214, 255)
GREY, GREY_LT, GREY_DK = (126, 138, 160, 255), (184, 194, 212, 255), (70, 80, 100, 255)
WHITE = (240, 246, 255, 255)
YELLOW = (255, 214, 60, 255)


class Grid:
    def __init__(self, w, h, fill=T):
        self.w, self.h = w, h
        self.p = [[fill] * w for _ in range(h)]

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.p[y][x] = c

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def disc(self, cx, cy, r, c):
        for y in range(self.h):
            for x in range(self.w):
                if (x + .5 - cx) ** 2 + (y + .5 - cy) ** 2 <= r * r:
                    self.set(x, y, c)

    def poly(self, pts, c):
        for y in range(self.h):
            yc = y + .5
            xs = []
            for i in range(len(pts)):
                (ax, ay), (bx, by) = pts[i], pts[(i + 1) % len(pts)]
                if (ay > yc) != (by > yc):
                    xs.append(ax + (yc - ay) * (bx - ax) / (by - ay))
            xs.sort()
            for a, b in zip(xs[::2], xs[1::2]):
                for x in range(self.w):
                    if a <= x + .5 <= b:
                        self.set(x, y, c)

    def outline(self, c=OUTLINE):
        """Adds a 1 px outline around every opaque pixel (4-neighbourhood)."""
        src = [row[:] for row in self.p]
        for y in range(self.h):
            for x in range(self.w):
                if src[y][x][3] == 0 and any(
                        0 <= x + dx < self.w and 0 <= y + dy < self.h and src[y + dy][x + dx][3] > 0
                        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    self.p[y][x] = c

    def image(self, k):
        img = Image(self.w * k, self.h * k, T)
        for y in range(self.h):
            for x in range(self.w):
                for yy in range(k):
                    for xx in range(k):
                        img.set(x * k + xx, y * k + yy, self.p[y][x])
        return img


def chamfer_frame(n, fill, border, light, inner, cut=2):
    """n×n panel: outline, coloured border with a lit top edge, inner dark line, chamfered corners."""
    g = Grid(n, n)

    def inside(x, y, inset):
        a, b = x - inset, y - inset
        m = n - 1 - 2 * inset
        if a < 0 or b < 0 or a > m or b > m:
            return False
        c = max(0, cut - inset)
        return not (a + b < c or (m - a) + b < c or a + (m - b) < c or (m - a) + (m - b) < c)

    for y in range(n):
        for x in range(n):
            if inside(x, y, 3):
                g.set(x, y, fill)
            elif inside(x, y, 2):
                g.set(x, y, inner)
            elif inside(x, y, 1):
                g.set(x, y, light if y <= 2 else border)
            elif inside(x, y, 0):
                g.set(x, y, OUTLINE)
    return g


def card_frame():
    g = chamfer_frame(24, NAVY, BLUE, BLUE_LT, BLUE_DK)
    for y in range(6, 18):            # accent notch on the left edge, as on the reference sheet
        g.set(1, y, CYAN if 9 <= y <= 14 else BLUE_LT)
    return g


def panel_frame():
    return chamfer_frame(24, DEEP, STEEL, STEEL_LT, (14, 26, 54, 255))


def button(fill, border, light):
    g = Grid(16, 12)
    for y in range(12):
        for x in range(16):
            corner = (x in (0, 15) and y in (0, 11))
            if corner:
                continue
            edge = x in (0, 15) or y in (0, 11)
            ring = x in (1, 14) or y in (1, 10)
            g.set(x, y, OUTLINE if edge else (light if ring and y == 1 else border) if ring else fill)
    for x in range(3, 13):
        g.set(x, 2, tuple(min(255, c + 18) for c in fill[:3]) + (fill[3],))
    return g


def bar(body, light, dark):
    g = Grid(12, 6)
    for y in range(6):
        for x in range(12):
            if (x in (0, 11) and y in (0, 5)):
                continue
            if x in (0, 11) or y in (0, 5):
                g.set(x, y, OUTLINE)
            else:
                g.set(x, y, light if y == 1 else dark if y == 4 else body)
    g.set(2, 1, WHITE)
    g.set(3, 1, WHITE)
    return g


def track():
    g = Grid(12, 6)
    for y in range(6):
        for x in range(12):
            if (x in (0, 11) and y in (0, 5)):
                continue
            g.set(x, y, OUTLINE if x in (0, 11) or y in (0, 5) else (3, 6, 15, 255) if y == 1 else (8, 14, 30, 255))
    return g


# ---------------------------------------------------------------- status symbols (12×12)
def status_ok():
    g = Grid(12, 12)
    g.poly([(3, 0), (9, 0), (12, 3), (12, 9), (9, 12), (3, 12), (0, 9), (0, 3)], GREEN)
    g.rect(3, 1, 8, 1, GREEN_LT)
    g.rect(1, 3, 1, 5, GREEN_LT)
    for x, y in ((3, 6), (4, 7), (5, 8), (6, 7), (7, 6), (8, 5), (9, 4)):
        g.set(x, y, WHITE)
        g.set(x, y - 1, WHITE)
    return g


def status_warn():
    g = Grid(12, 12)
    g.poly([(6, 0), (12, 11.5), (0, 11.5)], AMBER)
    g.rect(5, 4, 6, 7, OUTLINE)
    g.rect(5, 9, 6, 10, OUTLINE)
    g.set(6, 1, AMBER_LT)
    return g


def status_crit():
    g = Grid(12, 12)
    g.poly([(6, 0), (12, 6), (6, 12), (0, 6)], RED)
    g.rect(5, 3, 6, 6, WHITE)
    g.rect(5, 8, 6, 9, WHITE)
    g.set(6, 1, RED_LT)
    g.set(5, 2, RED_LT)
    return g


def status_none():
    g = Grid(12, 12)
    g.poly([(3, 0), (9, 0), (12, 3), (12, 9), (9, 12), (3, 12), (0, 9), (0, 3)], GREY)
    for y in range(12):
        for x in range(12):
            if g.p[y][x][3] and (x + y) % 4 < 2:
                g.set(x, y, GREY_DK)
    return g


# ---------------------------------------------------------------- resource icons (12×12)
def icon_battery():
    g = Grid(12, 12)
    g.rect(4, 0, 7, 1, BLUE_LT)
    g.rect(2, 1, 9, 11, BLUE)
    g.rect(3, 2, 8, 10, DEEP)
    g.rect(4, 6, 7, 9, CYAN)
    for x, y in ((6, 3), (5, 4), (6, 4), (5, 5), (6, 5), (7, 5), (6, 6), (5, 7)):
        g.set(x, y, YELLOW)
    return g


def icon_water():
    g = Grid(12, 12)
    g.poly([(6, 0), (10, 6), (2, 6)], BLUE)
    g.disc(6, 7.5, 4.2, BLUE)
    g.disc(6, 8, 3, (70, 150, 255, 255))
    g.rect(4, 6, 4, 8, CYAN_LT)
    g.set(5, 9, CYAN_LT)
    return g


def icon_food():
    g = Grid(12, 12)
    g.rect(1, 2, 10, 11, GREEN)
    g.rect(1, 2, 10, 4, GREEN_LT)
    g.rect(2, 6, 9, 10, GREEN_DK)
    g.rect(4, 7, 7, 9, GREEN_LT)
    g.rect(5, 0, 6, 2, GREEN_DK)
    return g


def icon_oxygen():
    g = Grid(12, 12)
    g.rect(4, 5, 7, 6, CYAN_DK)
    g.disc(3.5, 5.5, 3.2, CYAN)
    g.disc(8.5, 5.5, 3.2, CYAN)
    g.set(2, 4, CYAN_LT)
    g.set(7, 4, CYAN_LT)
    g.disc(9.5, 10, 1.6, CYAN_LT)
    return g


def icon_co2():
    g = Grid(12, 12)
    g.rect(5, 0, 6, 5, TEAL_LT)
    g.poly([(5, 3), (1, 5), (0, 10), (3, 12), (5, 10)], TEAL)
    g.poly([(7, 3), (11, 5), (12, 10), (9, 12), (7, 10)], TEAL)
    g.set(2, 6, TEAL_LT)
    g.set(9, 6, TEAL_LT)
    return g


def trend(kind):
    g = Grid(7, 5)
    if kind == "up":
        g.poly([(3.5, 0), (7, 5), (0, 5)], GREEN)
    elif kind == "down":
        g.poly([(0, 0), (7, 0), (3.5, 5)], AMBER)
    else:
        g.rect(0, 1, 6, 1, GREY_LT)
        g.rect(0, 3, 6, 3, GREY_LT)
    return g


def badge():
    """Mission badge: chamfered blue frame with a cyan ring and an astronaut helmet."""
    g = Grid(24, 24)
    g.poly([(3, 0), (21, 0), (24, 3), (24, 21), (21, 24), (3, 24), (0, 21), (0, 3)], OUTLINE)
    g.poly([(4, 1), (20, 1), (23, 4), (23, 20), (20, 23), (4, 23), (1, 20), (1, 4)], BLUE)
    g.poly([(4, 3), (20, 3), (21, 4), (21, 20), (20, 21), (4, 21), (3, 20), (3, 4)], DEEP)
    ring = Grid(24, 24)
    ring.disc(12, 12, 8.6, CYAN)
    ring.disc(12, 12, 7.4, T)
    for y in range(24):
        for x in range(24):
            if ring.p[y][x][3]:
                g.set(x, y, CYAN)
    g.disc(12, 12.5, 5.6, WHITE)
    g.disc(12, 12, 3.6, BLUE_DK)
    g.disc(12.5, 12.5, 2.6, BLUE)
    g.set(10, 10, CYAN_LT)
    g.set(11, 10, WHITE)
    g.rect(7, 16, 17, 17, GREY_LT)
    return g


# name -> (grid factory, scale, 9-slice border in output pixels or None)
ASSETS = {
    "frame-card": (card_frame, 2, 12),
    "frame-panel": (panel_frame, 2, 12),
    "button": (lambda: button((18, 32, 64, 245), (86, 128, 204, 255), BLUE_LT), 2, 6),
    "button-hover": (lambda: button((26, 48, 96, 250), (140, 190, 255, 255), WHITE), 2, 6),
    "button-active": (lambda: button((14, 64, 92, 250), CYAN, CYAN_LT), 2, 6),
    "button-primary": (lambda: button((22, 118, 64, 250), (96, 240, 132, 255), (210, 255, 220, 255)), 2, 6),
    "button-primary-hover": (lambda: button((34, 150, 84, 252), (190, 255, 200, 255), WHITE), 2, 6),
    "bar-track": (track, 2, 6),
    "bar-blue": (lambda: bar(BLUE, BLUE_LT, BLUE_DK), 2, 6),
    "bar-cyan": (lambda: bar(CYAN, CYAN_LT, CYAN_DK), 2, 6),
    "bar-green": (lambda: bar(GREEN, GREEN_LT, GREEN_DK), 2, 6),
    "bar-amber": (lambda: bar(AMBER, AMBER_LT, AMBER_DK), 2, 6),
    "bar-red": (lambda: bar(RED, RED_LT, RED_DK), 2, 6),
    "status-ok": (status_ok, 2, None),
    "status-warn": (status_warn, 2, None),
    "status-crit": (status_crit, 2, None),
    "status-none": (status_none, 2, None),
    "icon-battery": (icon_battery, 2, None),
    "icon-water": (icon_water, 2, None),
    "icon-food": (icon_food, 2, None),
    "icon-oxygen": (icon_oxygen, 2, None),
    "icon-co2": (icon_co2, 2, None),
    "trend-up": (lambda: trend("up"), 2, None),
    "trend-down": (lambda: trend("down"), 2, None),
    "trend-flat": (lambda: trend("flat"), 2, None),
    "badge-astronaut": (badge, 2, None),
}


def main():
    out = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT)
    os.makedirs(out, exist_ok=True)
    for name, (make, k, _border) in ASSETS.items():
        g = make()
        if name.startswith(("status", "icon", "trend")):
            padded = Grid(g.w + 2, g.h + 2)
            for y in range(g.h):
                for x in range(g.w):
                    padded.p[y + 1][x + 1] = g.p[y][x]
            g = padded
            g.outline()
        g.image(k).save(os.path.join(out, name + ".png"))
        print("wrote", name)


if __name__ == "__main__":
    main()
