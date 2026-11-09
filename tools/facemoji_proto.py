"""
Prototipe renderer karakter 3D (ray-cast elipsoid) untuk efek Facemoji.
Logika di sini sama dengan Facemoji.cs; dipakai untuk memeriksa tampilan karakter dan arah transformasi.
Jalankan: python tools/facemoji_proto.py  -> tools/facemoji_sheet.png dan tools/facemoji_expr.png
"""
import math, os
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))


def hexc(c):
    return np.array([(c >> 16) & 255, (c >> 8) & 255, c & 255], dtype=np.float32) / 255.0


def lerp(a, b, t):
    return a + (b - a) * t


class Expr:
    def __init__(self, **k):
        self.OpenA = 1; self.OpenB = 1; self.Jaw = 0; self.Smile = 0; self.Pucker = 0
        self.BrowA = 0; self.BrowB = 0; self.GazeX = 0; self.GazeY = 0; self.Swing = 0
        for a, b in k.items():
            setattr(self, a, b)


class Prim:
    def __init__(self, c, r, col, spec, shin, alpha, unlit):
        self.C = np.array(c, np.float32); self.R = np.array(r, np.float32); self.col = hexc(col)
        self.spec = spec; self.shin = shin; self.alpha = alpha; self.unlit = unlit


class Rig:
    def __init__(self, e):
        self.P = []; self.E = e

    def add(self, cx, cy, cz, rx, ry, rz, rgb, spec=0.3, shin=30, alpha=1.0, unlit=False):
        self.P.append(Prim((cx, cy, cz), (rx, ry, rz), rgb, spec, shin, alpha, unlit))


class Surf:
    def __init__(self, cx, cy, cz, rx, ry, rz):
        self.cx, self.cy, self.cz, self.rx, self.ry, self.rz = cx, cy, cz, rx, ry, rz

    def Z(self, x, y):
        q = 1 - (x - self.cx) ** 2 / self.rx ** 2 - (y - self.cy) ** 2 / self.ry ** 2
        return -1e6 if q <= 0 else self.cz + self.rz * math.sqrt(q)


def on(*surfs):
    def f(x, y):
        m = max(s.Z(x, y) for s in surfs)
        return 0.0 if m < -1e5 else m
    return f


def blob(g, cx, cy, cz, rx, ry, rz, col, spec=0.3, shin=30):
    g.add(cx, cy, cz, rx, ry, rz, col, spec, shin)
    return Surf(cx, cy, cz, rx, ry, rz)


def head(g, rx, ry, rz, col, spec=0.35):
    return blob(g, 0, 0, 0, rx, ry, rz, col, spec, 35)


def eyes(g, z, style, ex, ey, r, pupil=0x16161A, white=0xFFFFFF, zoff=0.0):
    e = g.E
    for side in (-1, 1):
        op = e.OpenA if side < 0 else e.OpenB
        x = side * ex; y = ey; zz = z(x, y) + zoff
        lid = lerp(0.10, 1.0, op)
        if style == 'ball':
            g.add(x, y, zz - 0.005, r, r * 1.15 * lid, 0.06, white, 0.5, 50)
            if op > 0.25:
                gx = e.GazeX * r * 0.45; gy = e.GazeY * r * 0.40
                g.add(x + gx, y + gy, zz + 0.045, r * 0.58, r * 0.62 * max(0.35, lid), 0.03, pupil, 0.9, 70)
                g.add(x + gx - r * 0.18, y + gy - r * 0.22, zz + 0.075, r * 0.14, r * 0.14, 0.02, 0xFFFFFF, 0, 1, 1, True)
            else:
                g.add(x, y + r * 0.05, zz + 0.03, r * 1.05, 0.012, 0.03, 0x2A2022, 0, 1, 1, True)
        elif style == 'dot':
            g.add(x, y, zz + 0.005, r * 0.7, r * 0.8 * lid, 0.05, pupil, 0.9, 70)
            if op > 0.4:
                g.add(x - r * 0.2, y - r * 0.25 * lid, zz + 0.05, r * 0.17, r * 0.17, 0.02, 0xFFFFFF, 0, 1, 1, True)
        elif style == 'big':
            g.add(x, y, zz, r * 1.35, r * 1.9 * lid, 0.07, pupil, 0.9, 80)
            if op > 0.4:
                g.add(x - r * 0.35, y - r * 0.6 * lid, zz + 0.06, r * 0.28, r * 0.28, 0.02, 0xFFFFFF, 0, 1, 1, True)


def nose(g, z, style, y, col, nostril=0x2A1A1C):
    zz = z(0, y)
    if style == 'round':
        g.add(0, y, zz, 0.075, 0.055, 0.06, col, 0.9, 70)
    elif style == 'tiny':
        g.add(0, y, zz, 0.04, 0.03, 0.04, col, 0.8, 60)
    elif style == 'snout':
        g.add(0, y + 0.02, zz + 0.03, 0.18, 0.13, 0.09, col, 0.5, 40)
        g.add(-0.06, y + 0.02, zz + 0.115, 0.025, 0.04, 0.02, nostril, 0.2, 10)
        g.add(0.06, y + 0.02, zz + 0.115, 0.025, 0.04, 0.02, nostril, 0.2, 10)


def mouth(g, z, my, half0, inner=0x5A1822, line=0x2B1A1C, tongue=0xE2707C):
    e = g.E
    half = half0 * (1 + 0.35 * e.Smile - 0.45 * e.Pucker)
    zf = z(0, my)
    if e.Jaw > 0.12:
        ry = 0.025 + 0.16 * e.Jaw
        g.add(0, my + ry * 0.5, zf + 0.01, half * (0.9 - 0.25 * e.Pucker), ry, 0.05, inner, 0, 1, 1, True)
        g.add(0, my + ry * 0.95, zf + 0.025, half * 0.5, ry * 0.45, 0.04, tongue, 0.3, 20)
    else:
        for i in range(11):
            u = i / 5.0 - 1
            x = u * half
            y = my + 0.03 * e.Smile - 0.06 * e.Smile * u * u
            g.add(x, y, z(x, y) + 0.005, 0.022, 0.016, 0.02, line, 0.3, 20)


def brows(g, z, col, ex, ey, ln=0.12, thick=0.03):
    e = g.E
    for side in (-1, 1):
        br = e.BrowA if side < 0 else e.BrowB
        for i in range(5):
            u = (i - 2) / 2.0
            x = side * ex + u * ln * 0.55
            inner = -side * u
            tilt = max(0.0, -br) * 0.05 * inner
            y = ey - 0.17 - 0.07 * br + tilt - 0.02 * (1 - abs(u)) * max(0.0, br)
            g.add(x, y, z(x, y), ln * 0.30, thick, 0.03, col, 0.2, 20)


def blush(g, z, col, x=0.40, y=0.16):
    a = 0.35 + 0.4 * g.E.Smile
    for s in (-1, 1):
        xx = s * x
        g.add(xx, y, z(xx, y) + 0.005, 0.09, 0.055, 0.02, col, 0, 1, a, True)


# ---------------------------------------------------------------- karakter
def bear(g):
    h = head(g, .62, .68, .60, 0xA0693A); sw = g.E.Swing * .04
    for s in (-1, 1):
        g.add(s * .46 + sw * s, -.56, -.02, .18, .18, .12, 0xA0693A)
        g.add(s * .46 + sw * s, -.56, .07, .105, .105, .06, 0xE0B88A)
    mz = blob(g, 0, .22, h.Z(0, .22) - .03, .23, .18, .12, 0xE4C49C); z = on(h, mz)
    eyes(g, z, 'dot', .25, -.08, .10); brows(g, z, 0x5A3A1C, .25, -.08)
    nose(g, z, 'round', .13, 0x2A1A1C); mouth(g, z, .31, .12); blush(g, z, 0xFF9AA2)


def cat(g):
    h = head(g, .64, .58, .55, 0xE8944A); sw = g.E.Swing * .04
    for s in (-1, 1):
        g.add(s * .40 + sw * s, -.60, 0, .13, .24, .08, 0xE8944A)
        g.add(s * .40 + sw * s, -.58, .055, .07, .15, .05, 0xF5A0A8)
    for x in (-.14, 0, .14):
        g.add(x, -.40, h.Z(x, -.40), .025, .08, .03, 0xB5651D, 0, 1, .85, True)
    mz = blob(g, 0, .20, h.Z(0, .20) - .02, .18, .13, .09, 0xFFF1DC); z = on(h, mz)
    eyes(g, z, 'ball', .27, -.06, .105, pupil=0x2E7D32); nose(g, z, 'tiny', .12, 0xF08A9A); mouth(g, z, .27, .09)
    for k in (-1, 0, 1):
        for s in (-1, 1):
            g.add(s * .46, .20 + k * .05, h.Z(s * .30, .20) + .03, .20, .007, .01, 0xFFFFFF, 0, 1, .9, True)
    brows(g, z, 0xB5651D, .27, -.06, .1, .022)


def dog(g):
    h = head(g, .62, .66, .58, 0xC8924B); sw = g.E.Swing * .05
    for s in (-1, 1):
        g.add(s * .62 + sw * s, -.10, -.05, .14, .34, .12, 0x7A4A21)
    mz = blob(g, 0, .22, h.Z(0, .22) - .03, .24, .18, .12, 0xF2DDB8); z = on(h, mz)
    eyes(g, z, 'ball', .26, -.10, .10); brows(g, z, 0x6B431C, .26, -.10)
    nose(g, z, 'round', .14, 0x1E1416); mouth(g, z, .32, .13); blush(g, z, 0xFF9AA2, .42, .12)


def fox(g):
    h = head(g, .62, .56, .55, 0xE2742B); sw = g.E.Swing * .04
    for s in (-1, 1):
        g.add(s * .40 + sw * s, -.64, 0, .12, .26, .07, 0xE2742B)
        g.add(s * .40 + sw * s, -.66, .06, .07, .15, .04, 0x2A2A2A)
        g.add(s * .30, .22, h.Z(s * .30, .22) - .05, .16, .11, .09, 0xFFFFFF)
    mz = blob(g, 0, .20, h.Z(0, .20) - .02, .16, .13, .13, 0xFFFFFF); z = on(h, mz)
    eyes(g, z, 'ball', .26, -.07, .10, pupil=0x3A2A10); brows(g, z, 0x8A3C10, .26, -.07)
    nose(g, z, 'round', .16, 0x1A1214); mouth(g, z, .30, .10)


def bunny(g):
    h = head(g, .60, .62, .56, 0xF3F0EC); sw = g.E.Swing * .06
    for s in (-1, 1):
        g.add(s * .22 + sw * s, -1.0, 0, .12, .46, .07, 0xF3F0EC)
        g.add(s * .22 + sw * s, -.98, .05, .06, .36, .04, 0xF5B5C5)
    z = on(h)
    eyes(g, z, 'dot', .24, -.06, .10); brows(g, z, 0xBBB4AA, .24, -.06)
    nose(g, z, 'tiny', .12, 0xF08FA5); mouth(g, z, .26, .09)
    if g.E.Jaw < .15:
        for s in (-1, 1):
            g.add(s * .035, .36 + g.E.Jaw * .1, z(0, .36) + .01, .035, .055, .03, 0xFFFFFF, .4, 40)
    blush(g, z, 0xFFA9B8)


def panda(g):
    h = head(g, .64, .64, .58, 0xF6F6F6)
    for s in (-1, 1):
        g.add(s * .46, -.56, -.02, .17, .17, .12, 0x1B1B1D)
        g.add(s * .26, -.06, h.Z(s * .26, -.06) - .02, .15, .19, .08, 0x1B1B1D)
    z = on(h)
    eyes(g, z, 'ball', .26, -.06, .075, zoff=.04)
    nose(g, z, 'round', .15, 0x1B1B1D); mouth(g, z, .30, .11); blush(g, z, 0xFFA9B8, .45, .20)


def pig(g):
    h = head(g, .66, .62, .58, 0xF7A8B8); sw = g.E.Swing * .05
    for s in (-1, 1):
        g.add(s * .48 + sw * s, -.52, .02, .16, .15, .07, 0xE88C9F)
    z = on(h)
    eyes(g, z, 'dot', .26, -.12, .10); nose(g, z, 'snout', .13, 0xF08FA5); mouth(g, z, .38, .13, line=0x8A3A4A)
    blush(g, z, 0xFF7E94, .46, .12)


def monkey(g):
    h = head(g, .62, .62, .56, 0x8A5A3B)
    for s in (-1, 1):
        g.add(s * .66, -.02, -.02, .15, .15, .10, 0x8A5A3B)
        g.add(s * .66, -.02, .06, .09, .09, .05, 0xE8C39E)
    p1 = blob(g, -.17, -.05, h.Z(-.17, -.05) - .06, .22, .20, .12, 0xE9C29B)
    p2 = blob(g, .17, -.05, h.Z(.17, -.05) - .06, .22, .20, .12, 0xE9C29B)
    p3 = blob(g, 0, .22, h.Z(0, .22) - .05, .30, .25, .12, 0xE9C29B); z = on(h, p1, p2, p3)
    eyes(g, z, 'ball', .22, -.06, .09, zoff=.01); brows(g, z, 0x4A2E1A, .22, -.06)
    for s in (-1, 1):
        g.add(s * .035, .14, z(0, .14) + .02, .02, .028, .02, 0x4A2E1A, .5, 30)
    mouth(g, z, .30, .12, line=0x4A2E1A)


def lion(g):
    h = head(g, .55, .58, .55, 0xE0A64A)
    for i in range(14):
        a = i / 14 * math.tau
        g.add(math.cos(a) * .70, math.sin(a) * .74, -.10, .22, .22, .20, 0xB0701E)
    for s in (-1, 1):
        g.add(s * .38, -.55, .02, .10, .10, .07, 0xE0A64A)
        g.add(s * .38, -.55, .07, .06, .06, .04, 0xE9B5A0)
    mz = blob(g, 0, .20, h.Z(0, .20) - .02, .20, .15, .10, 0xF6E2B5); z = on(h, mz)
    eyes(g, z, 'ball', .22, -.07, .09); brows(g, z, 0x8A5A18, .22, -.07)
    nose(g, z, 'round', .12, 0x3A2A24); mouth(g, z, .29, .10)


def frog(g):
    h = head(g, .68, .52, .55, 0x5FBF4A); e = g.E
    for s in (-1, 1):
        op = e.OpenA if s < 0 else e.OpenB
        g.add(s * .32, -.48, .20, .19, .19, .17, 0x5FBF4A)
        g.add(s * .32, -.48, .34, .13, .13 * lerp(.12, 1, op), .06, 0xFFFFFF, .5, 50)
        if op > .25:
            g.add(s * .32 + e.GazeX * .05, -.48 + e.GazeY * .04, .40, .065, .07 * max(.35, op), .03, 0x16161A, .9, 70)
    z = on(h)
    for s in (-1, 1):
        g.add(s * .05, .02, z(s * .05, .02) + .005, .015, .015, .02, 0x2A4A24)
    mouth(g, z, .30, .30, line=0x2A4A24); blush(g, z, 0xFF8FA0, .45, .18)


def alien(g):
    h = head(g, .60, .78, .55, 0x8FD16A)
    for s in (-1, 1):
        g.add(s * .22, -.88, -.02, .015, .12, .015, 0x6BAA4A)
        g.add(s * .22, -1.0, -.02, .06, .06, .06, 0xB6FF8A, 0, 1, 1, True)
    z = on(h)
    eyes(g, z, 'big', .26, -.02, .13, pupil=0x0E0E16)
    for s in (-1, 1):
        g.add(s * .03, .20, z(s * .03, .20) + .005, .012, .02, .02, 0x4A8A32)
    mouth(g, z, .42, .10, line=0x3A6A28)


def robot(g):
    h = head(g, .66, .62, .56, 0xB8C4D0, .6); e = g.E
    g.add(0, -.70, 0, .018, .10, .018, 0x8895A3); g.add(0, -.82, 0, .055, .055, .055, 0xFF4A4A, 0, 1, 1, True)
    for s in (-1, 1):
        g.add(s * .70, 0, 0, .10, .16, .12, 0x8895A3)
    z = on(h)
    for s in (-1, 1):
        op = e.OpenA if s < 0 else e.OpenB
        g.add(s * .27, -.08, z(s * .27, -.08) + .005, .13, .02 + .09 * op, .03, 0x39D0FF, 0, 1, 1, True)
    for i in range(5):
        x = (i - 2) * .075
        g.add(x, .30 + .04 * e.Jaw, z(x, .30) + .005, .032, .02 + .10 * e.Jaw, .03, 0x39D0FF, 0, 1, 1, True)
    for s in (-1, 1):
        g.add(s * .52, .30, z(s * .52, .30) + .005, .025, .025, .02, 0x6A7684)


def ghost(g):
    h = head(g, .66, .78, .55, 0xF5F5FF, .5)
    z = on(h); e = g.E
    for s in (-1, 1):
        op = e.OpenA if s < 0 else e.OpenB
        g.add(s * .22, -.10, z(s * .22, -.10) + .01, .085, .15 * lerp(.12, 1, op), .04, 0x18181F, .6, 60)
    ry = .05 + .20 * e.Jaw
    g.add(0, .22 + ry * .5, z(0, .22) + .01, .11, ry, .04, 0x18181F, 0, 1, 1, True)
    blush(g, z, 0xFFB0C0, .38, .14)


def unicorn(g):
    h = head(g, .60, .64, .56, 0xF6EEF8); sw = g.E.Swing * .04
    cols = [0xFF8FC8, 0xB78CFF, 0x7FC8FF, 0xFF8FC8, 0xB78CFF]
    for i, c in enumerate(cols):
        g.add(.52, -.40 + i * .22, -.12, .14, .16, .12, c)
        g.add(-.52, -.40 + i * .22, -.12, .14, .16, .12, c)
    for i in range(5):
        g.add(0, -.62 - i * .10, h.Z(0, -.5) * .6, .095 - i * .012, .08, .085 - i * .012, 0xFFD36B, .8, 60)
    for s in (-1, 1):
        g.add(s * .36 + sw * s, -.62, -.02, .09, .16, .07, 0xF6EEF8)
        g.add(s * .36 + sw * s, -.60, .04, .05, .10, .04, 0xF5B5C5)
    z = on(h)
    eyes(g, z, 'ball', .24, -.05, .10, pupil=0x7A4FD0)
    nose(g, z, 'tiny', .13, 0xE8A0C0); mouth(g, z, .28, .10); blush(g, z, 0xFF9AB8)


def skull(g):
    h = head(g, .60, .66, .56, 0xF1EFE6); e = g.E
    z = on(h)
    for s in (-1, 1):
        op = e.OpenA if s < 0 else e.OpenB
        g.add(s * .25, -.05, z(s * .25, -.05) + .01, .15, .17 * lerp(.3, 1, op), .08, 0x141416, 0, 1, 1, True)
        g.add(s * .25 + e.GazeX * .04, -.05 + e.GazeY * .04, z(s * .25, -.05) + .08, .03, .03, .02, 0xFFFFFF, 0, 1, 1, True)
    g.add(0, .15, z(0, .15) + .01, .05, .07, .05, 0x141416, 0, 1, 1, True)
    j = e.Jaw * .18
    g.add(0, .36 + j * .5, z(0, .36) + .01, .25, .02 + j * .6, .04, 0x141416, 0, 1, 1, True)
    for i in range(6):
        x = (i - 2.5) * .075
        g.add(x, .335, z(x, .335) + .03, .035, .045, .03, 0xFFFFFF, .4, 40)
        g.add(x, .39 + j, z(x, .39) + .03, .035, .045, .03, 0xFFFFFF, .4, 40)


def chicken(g):
    h = head(g, .62, .64, .58, 0xFFFFFF)
    for x, y in ((0, -.72), (-.13, -.68), (.13, -.68)):
        g.add(x, y, 0, .10, .10, .08, 0xE02626)
    z = on(h); e = g.E
    eyes(g, z, 'dot', .25, -.08, .10)
    zf = z(0, .14)
    g.add(0, .14, zf + .12, .13, .06, .15, 0xFFA21A, .5, 40)
    g.add(0, .22 + e.Jaw * .12, zf + .10, .10, .04, .12, 0xFF9A10, .5, 40)
    g.add(0, .36 + e.Jaw * .12, zf, .05, .08, .05, 0xE02626)
    blush(g, z, 0xFFB0B8, .42, .14)


def koala(g):
    h = head(g, .62, .62, .58, 0x9AA0A6)
    for s in (-1, 1):
        g.add(s * .55, -.50, -.04, .27, .27, .15, 0x9AA0A6)
        g.add(s * .55, -.50, .10, .16, .16, .07, 0xE8E8EA)
    z = on(h)
    eyes(g, z, 'dot', .25, -.06, .08)
    g.add(0, .12, z(0, .12), .11, .15, .07, 0x2A2A2E, .9, 70)
    mouth(g, z, .36, .11); blush(g, z, 0xFFB0B8, .43, .17)


def dino(g):
    h = head(g, .60, .60, .56, 0x6CCB5F)
    for i, (x, y) in enumerate(((-.30, -.52), (-.15, -.65), (0, -.70), (.15, -.65), (.30, -.52))):
        g.add(x, y, 0, .09, .09, .08, 0xF2E394)
    mz = blob(g, 0, .20, h.Z(0, .20) - .02, .30, .20, .20, 0x8FDD7E); z = on(h, mz)
    eyes(g, z, 'ball', .24, -.10, .095, zoff=.0); brows(g, z, 0x3E8A34, .24, -.10)
    for s in (-1, 1):
        g.add(s * .07, .16, z(s * .07, .16) + .01, .02, .028, .02, 0x2A5A24)
    mouth(g, z, .32, .16, line=0x2A5A24)


CHARS = [bear, cat, dog, fox, bunny, panda, pig, monkey, lion, frog, alien, robot, ghost, unicorn, skull, chicken, koala, dino]


# ---------------------------------------------------------------- renderer
def basis(yaw=0, pitch=0, roll=0):
    U = np.array([1, 0, 0.]); V = np.array([0, 1, 0.]); N = np.array([0, 0, -1.])
    cy, sy = math.cos(yaw), math.sin(yaw); cp, sp = math.cos(pitch), math.sin(pitch); cr, sr = math.cos(roll), math.sin(roll)
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]); Rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]])
    Rz = np.array([[cr, -sr, 0], [sr, cr, 0], [0, 0, 1]])
    R = Rz @ Ry @ Rx
    return R @ U, R @ V, R @ N


LIGHT = np.array([-.40, -.60, -.70]); LIGHT /= np.linalg.norm(LIGHT)
VIEW = np.array([0, 0, -1.]); HALF = LIGHT + VIEW; HALF /= np.linalg.norm(HALF)


def render(prims, U, V, N, S, W, H, cx, cy):
    Zb = np.full((H, W), 1e9, np.float32); A = np.zeros((H, W), np.float32); C = np.zeros((H, W, 3), np.float32)
    invS = 1.0 / S
    dirL = np.array([U[2], V[2], N[2]], np.float32) * invS
    for p in prims:
        pc = np.array([cx, cy]) + (p.C[0] * U[:2] + p.C[1] * V[:2] + p.C[2] * N[:2]) * S
        rad = float(p.R.max()) * S + 2
        x0 = max(0, int(pc[0] - rad)); x1 = min(W, int(pc[0] + rad) + 1)
        y0 = max(0, int(pc[1] - rad)); y1 = min(H, int(pc[1] + rad) + 1)
        if x1 <= x0 or y1 <= y0:
            continue
        ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
        dx = xs + .5 - cx; dy = ys + .5 - cy
        ol = np.stack([U[0] * dx + U[1] * dy, V[0] * dx + V[1] * dy, N[0] * dx + N[1] * dy], -1) * invS
        invR = 1.0 / p.R
        op = (ol - p.C) * invR
        dp = dirL * invR
        a = float(dp @ dp)
        b = op @ dp
        m = (op * op).sum(-1) - 1 - b * b / a
        rpx = S * float(np.prod(p.R)) ** (1 / 3)
        edge = 2.0 / rpx
        al = np.clip(0.5 - m * rpx * 0.5, 0, 1) * p.alpha
        mask = (m < edge) & (al > 0.01)
        if not mask.any():
            continue
        t = -b / a - np.where(m < 0, np.sqrt(np.maximum(-m / a, 0)), 0)
        pp = op + t[..., None] * dp
        nl = pp * invR
        nl /= np.maximum(np.linalg.norm(nl, axis=-1, keepdims=True), 1e-6)
        ncam = nl[..., 0:1] * U + nl[..., 1:2] * V + nl[..., 2:3] * N
        if p.unlit:
            col = np.broadcast_to(p.col, ncam.shape).copy()
        else:
            diff = np.clip(ncam @ LIGHT, 0, 1)
            spec = np.clip(ncam @ HALF, 0, 1) ** p.shin * p.spec
            rim = (1 - np.clip(-ncam[..., 2], 0, 1)) ** 3 * 0.12
            col = p.col * (0.36 + 0.64 * diff)[..., None] + (spec + rim)[..., None]
        col = np.clip(col, 0, 1)
        zs = Zb[y0:y1, x0:x1]; As = A[y0:y1, x0:x1]; Cs = C[y0:y1, x0:x1]
        near = mask & (t < zs)
        far = mask & ~near
        a3 = al[..., None]
        newC = np.where(near[..., None], col * a3 + Cs * (1 - a3), Cs)
        room = (1 - As)
        addf = al * room
        newC = np.where(far[..., None], Cs + col * addf[..., None], newC)
        newA = np.where(near, al + As * (1 - al), As)
        newA = np.where(far, As + addf, newA)
        newZ = np.where(near & (al > .5), t, zs)
        Zb[y0:y1, x0:x1] = newZ; A[y0:y1, x0:x1] = newA; C[y0:y1, x0:x1] = newC
    return C, A


def draw_char(fn, expr, yaw=0.0, pitch=0.0, roll=0.0, size=380, bg=(78, 92, 110)):
    g = Rig(expr); fn(g)
    U, V, N = basis(yaw, pitch, roll)
    S = size * 0.55
    C, A = render(g.P, U, V, N, S, size, size, size / 2, size * 0.56)
    bgc = np.array(bg, np.float32) / 255
    out = C + bgc * (1 - A[..., None])
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8))


def sheet():
    cols = 6
    tiles = []
    for fn in CHARS:
        e = Expr(Smile=.35, GazeX=.2)
        im = draw_char(fn, e, yaw=math.radians(12), pitch=math.radians(-4))
        ImageDraw.Draw(im).text((8, 6), fn.__name__, fill=(255, 255, 255))
        tiles.append(im)
    rows = math.ceil(len(tiles) / cols)
    W = tiles[0].width
    sh = Image.new("RGB", (cols * W, rows * W))
    for i, t in enumerate(tiles):
        sh.paste(t, ((i % cols) * W, (i // cols) * W))
    sh.resize((sh.width * 2 // 3, sh.height * 2 // 3), Image.LANCZOS).save(os.path.join(HERE, "facemoji_sheet.png"))


def expr_sheet():
    exprs = [("netral", Expr(), 0, 0, 0), ("mulut buka + alis naik", Expr(Jaw=.8, BrowA=.8, BrowB=.8, Smile=.3), 0, 0, 0),
             ("kedip + senyum", Expr(OpenA=0, OpenB=.1, Smile=1), 0, 0, 0), ("marah + menoleh 30", Expr(BrowA=-1, BrowB=-1, Jaw=.3), math.radians(30), 0, 0),
             ("miring + lihat kanan", Expr(Smile=.4, GazeX=1), 0, math.radians(10), math.radians(15))]
    chars = [bear, frog, robot, skull]
    W = 300
    sh = Image.new("RGB", (W * len(exprs), W * len(chars)))
    for r, fn in enumerate(chars):
        for c, (name, e, yw, pt, rl) in enumerate(exprs):
            im = draw_char(fn, e, yw, pt, rl, size=W)
            if r == 0:
                ImageDraw.Draw(im).text((6, 6), name, fill=(255, 255, 255))
            sh.paste(im, (c * W, r * W))
    sh.save(os.path.join(HERE, "facemoji_expr.png"))


if __name__ == "__main__":
    sheet()
    expr_sheet()
    print("ok")
