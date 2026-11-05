"""
Membuat stiker AR (PNG transparan, gambar asli) dan menghasilkan ArCatalog.cs.
Jalankan:  python tools/make_stickers.py        (butuh Pillow)
Opsi:      python tools/make_stickers.py --mock  -> membuat tools/mock_preview.png untuk cek penempatan
"""
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter

SS = 2
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Assets", "Stickers")
os.makedirs(OUT, exist_ok=True)


def ellipse_pts(cx, cy, rx, ry, ang=0.0, n=90):
    a = math.radians(ang)
    ca, sa = math.cos(a), math.sin(a)
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        x, y = rx * math.cos(t), ry * math.sin(t)
        pts.append((cx + x * ca - y * sa, cy + x * sa + y * ca))
    return pts


def bezier(p0, p1, p2, p3, n=28):
    pts = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        x = u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0]
        y = u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1]
        pts.append((x, y))
    return pts


class C:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.im = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.im)

    def poly(self, pts, fill, outline=None, width=0):
        P = [(x * SS, y * SS) for x, y in pts]
        self.d.polygon(P, fill=fill)
        if outline and width:
            self.d.line(P + [P[0]], fill=outline, width=int(width * SS), joint="curve")

    def ell(self, cx, cy, rx, ry, fill, ang=0, outline=None, width=0):
        self.poly(ellipse_pts(cx, cy, rx, ry, ang), fill, outline, width)

    def circle(self, cx, cy, r, fill, outline=None, width=0):
        self.ell(cx, cy, r, r, fill, 0, outline, width)

    def line(self, pts, fill, width):
        P = [(x * SS, y * SS) for x, y in pts]
        self.d.line(P, fill=fill, width=int(width * SS), joint="curve")
        r = width * SS / 2
        for x, y in (P[0], P[-1]):  # ujung membulat
            self.d.ellipse((x - r, y - r, x + r, y + r), fill=fill)

    def rrect(self, x0, y0, x1, y1, r, fill, outline=None, width=0):
        self.d.rounded_rectangle((x0 * SS, y0 * SS, x1 * SS, y1 * SS), radius=r * SS, fill=fill,
                                 outline=outline, width=int(width * SS))

    def glow(self, radius):
        g = self.im.filter(ImageFilter.GaussianBlur(radius * SS))
        self.im = Image.alpha_composite(g, self.im)
        self.d = ImageDraw.Draw(self.im)

    def save(self, name):
        im = self.im.resize((self.w, self.h), Image.LANCZOS)
        im.save(os.path.join(OUT, name + ".png"))


DARK = (30, 28, 32, 255)
WHITE = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)


def dog_ears():
    c = C(900, 520)
    for s in (-1, 1):
        cx = 450 + s * 270
        c.ell(cx, 300, 105, 215, (112, 70, 38, 255), ang=-s * 14, outline=(72, 44, 22, 255), width=8)
        c.ell(cx + s * 4, 312, 62, 160, (170, 116, 76, 255), ang=-s * 14)
    c.save("dog_ears")


def dog_nose():
    c = C(300, 230)
    c.ell(150, 80, 115, 75, (28, 24, 24, 255))
    c.ell(112, 55, 40, 18, (255, 255, 255, 170), ang=-15)
    c.line([(150, 150), (150, 188)], (40, 32, 32, 255), 9)
    c.line(bezier((150, 188), (130, 225), (80, 225), (45, 195)), (40, 32, 32, 255), 9)
    c.line(bezier((150, 188), (170, 225), (220, 225), (255, 195)), (40, 32, 32, 255), 9)
    c.save("dog_nose")


def dog_tongue():
    c = C(240, 320)
    c.poly([(40, 0), (200, 0), (200, 210), (40, 210)], (240, 98, 130, 255))
    c.ell(120, 210, 80, 100, (240, 98, 130, 255))
    c.ell(120, 215, 62, 78, (255, 130, 160, 120))
    c.line([(120, 30), (120, 250)], (200, 60, 95, 255), 7)
    c.save("dog_tongue")


def cat_ears():
    c = C(900, 420)
    for s in (-1, 1):
        def X(x):
            return x if s == -1 else 900 - x
        c.poly([(X(70), 420), (X(110), 40), (X(360), 330)], (72, 72, 82, 255), (35, 35, 42, 255), 10)
        c.poly([(X(135), 340), (X(150), 140), (X(280), 320)], (255, 160, 180, 255))
    c.save("cat_ears")


def cat_whiskers():
    c = C(1000, 420)
    c.poly([(440, 170), (560, 170), (500, 232)], (255, 140, 165, 255), (190, 80, 105, 255), 6)
    c.line([(500, 232), (500, 262)], (60, 40, 45, 255), 7)
    c.line(bezier((500, 262), (480, 295), (450, 295), (430, 270)), (60, 40, 45, 255), 7)
    c.line(bezier((500, 262), (520, 295), (550, 295), (570, 270)), (60, 40, 45, 255), 7)
    for k in range(3):
        y1 = 215 + k * 24
        y2 = 150 + k * 62
        for s in (-1, 1):
            x1 = 500 + s * 120
            x2 = 500 + s * 460
            c.line([(x1, y1), (x2, y2)], (255, 255, 255, 150), 13)
            c.line([(x1, y1), (x2, y2)], (55, 55, 62, 255), 6)
    c.save("cat_whiskers")


def bunny_ears():
    c = C(560, 900)
    for s in (-1, 1):
        cx = 280 + s * 105
        c.ell(cx, 540, 100, 340, (245, 245, 250, 255), ang=s * 8, outline=(170, 170, 185, 255), width=8)
        c.ell(cx, 550, 55, 270, (255, 175, 195, 255), ang=s * 8)
    c.save("bunny_ears")


def bunny_nose():
    c = C(240, 200)
    c.ell(120, 60, 55, 38, (255, 130, 160, 255), outline=(200, 80, 110, 255), width=5)
    c.ell(105, 48, 16, 8, (255, 255, 255, 170), ang=-15)
    c.line([(120, 98), (120, 132)], (110, 70, 80, 255), 6)
    c.line(bezier((120, 132), (110, 165), (90, 170), (70, 160)), (110, 70, 80, 255), 6)
    c.line(bezier((120, 132), (130, 165), (150, 170), (170, 160)), (110, 70, 80, 255), 6)
    c.save("bunny_nose")


def bunny_teeth():
    c = C(200, 240)
    c.rrect(22, -30, 98, 200, 26, WHITE, (150, 150, 165, 255), 6)
    c.rrect(102, -30, 178, 200, 26, WHITE, (150, 150, 165, 255), 6)
    c.save("bunny_teeth")


def glasses_round():
    c = C(1000, 440)
    d = (35, 35, 40, 255)
    c.line([(112, 195), (8, 165)], d, 18)
    c.line([(888, 195), (992, 165)], d, 18)
    c.line(bezier((425, 190), (465, 140), (535, 140), (575, 190)), d, 18)
    for cx in (270, 730):
        c.circle(cx, 220, 162, d)
        c.circle(cx, 220, 140, CLEAR)
        c.circle(cx, 220, 140, (205, 232, 255, 70))
        c.line([(cx - 90, 160), (cx - 40, 110)], (255, 255, 255, 140), 14)
    c.save("glasses_round")


def sunglasses():
    c = C(1000, 440)
    d = (12, 12, 16, 255)
    c.line([(110, 170), (8, 150)], d, 20)
    c.line([(890, 170), (992, 150)], d, 20)
    c.line(bezier((425, 150), (465, 120), (535, 120), (575, 150)), d, 22)
    for x0, x1 in ((105, 445), (555, 895)):
        c.rrect(x0, 100, x1, 340, 100, (18, 18, 28, 245), d, 12)
        c.line([(x0 + 70, 290), (x0 + 150, 150)], (255, 255, 255, 90), 18)
        c.line([(x0 + 110, 300), (x0 + 160, 215)], (255, 255, 255, 60), 10)
    c.save("sunglasses")


def crown():
    c = C(760, 560)
    gold, dgold = (246, 192, 48, 255), (214, 150, 24, 255)
    pts = [(40, 540), (40, 120), (210, 320), (380, 40), (550, 320), (720, 120), (720, 540)]
    c.poly(pts, gold, (150, 100, 10, 255), 10)
    c.poly([(40, 430), (720, 430), (720, 540), (40, 540)], dgold, (150, 100, 10, 255), 8)
    for x, y in ((40, 110), (380, 30), (720, 110)):
        c.circle(x, y, 30, (250, 250, 255, 255), (170, 170, 190, 255), 5)
    c.ell(380, 320, 36, 44, (220, 30, 60, 255), outline=(120, 10, 30, 255), width=5)
    for x in (190, 570):
        c.ell(x, 485, 26, 26, (40, 120, 230, 255), outline=(20, 60, 140, 255), width=5)
    c.save("crown")


def party_hat():
    c = C(380, 560)
    c.poly([(190, 10), (18, 520), (362, 520)], (232, 62, 150, 255), (150, 30, 95, 255), 8)
    for (y0, y1, col) in ((110, 160, (255, 220, 60, 255)), (240, 290, (70, 200, 240, 255)),
                          (370, 420, (255, 220, 60, 255))):
        h0 = (y0 - 10) / 510 * 172
        h1 = (y1 - 10) / 510 * 172
        c.poly([(190 - h0, y0), (190 + h0, y0), (190 + h1, y1), (190 - h1, y1)], col)
    c.ell(190, 522, 172, 26, (255, 220, 60, 255), outline=(190, 150, 20, 255), width=5)
    c.circle(190, 22, 34, (255, 235, 90, 255), (200, 160, 20, 255), 5)
    c.save("party_hat")


def mustache():
    c = C(800, 300)
    col = (52, 34, 26, 255)
    c.ell(400, 150, 72, 56, col)
    for s in (-1, 1):
        cx = 400 + s * 150
        c.ell(cx, 150, 170, 60, col, ang=s * 22 * -1)
        c.ell(400 + s * 330, 98, 60, 28, col, ang=s * 60 * -1)
        c.ell(400 + s * 100, 130, 80, 22, (95, 66, 50, 160), ang=s * 10 * -1)
    c.save("mustache")


def heart_pts(cx, cy, size):
    pts = []
    for i in range(120):
        t = 2 * math.pi * i / 120
        x = 16 * math.sin(t) ** 3
        y = -(13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t))
        pts.append((cx + x * size, cy + y * size))
    return pts


def heart():
    c = C(300, 280)
    c.poly(heart_pts(150, 140, 8.2), (236, 40, 78, 255), (160, 16, 50, 255), 8)
    c.ell(95, 85, 34, 17, (255, 255, 255, 150), ang=-35)
    c.save("heart")


def devil_horns():
    c = C(900, 380)
    for s in (-1, 1):
        def X(x):
            return x if s == -1 else 900 - x
        outer = bezier((X(150), 380), (X(70), 300), (X(50), 150), (X(130), 30))
        inner = bezier((X(130), 30), (X(210), 150), (X(250), 260), (X(290), 380))
        c.poly(outer + inner, (205, 28, 38, 255), (110, 10, 16, 255), 8)
        c.line(bezier((X(150), 330), (X(120), 250), (X(115), 160), (X(135), 90)), (255, 120, 120, 120), 10)
    c.save("devil_horns")


def halo():
    c = C(640, 220)
    c.ell(320, 110, 280, 70, (255, 214, 60, 255))
    c.ell(320, 110, 235, 42, CLEAR)
    c.ell(320, 96, 250, 52, (255, 245, 170, 130), outline=None)
    c.ell(320, 110, 235, 42, CLEAR)
    c.glow(12)
    c.save("halo")


def clown_nose():
    c = C(240, 240)
    c.circle(120, 120, 100, (224, 28, 38, 255), (140, 10, 20, 255), 6)
    c.ell(88, 82, 34, 20, (255, 255, 255, 170), ang=-35)
    c.save("clown_nose")


def clown_wig():
    c = C(1400, 640)
    base, hi, edge = (255, 96, 32, 255), (255, 150, 70, 255), (190, 60, 10, 255)
    clusters = [(230, 330, 150), (190, 200, 118), (270, 470, 112), (140, 400, 96), (330, 220, 100),
                (700, 120, 112), (585, 150, 92), (815, 150, 92)]
    allc = []
    for x, y, r in clusters:
        allc.append((x, y, r))
        if x != 700 and x != 585 and x != 815:
            allc.append((1400 - x, y, r))
    for x, y, r in allc:
        c.circle(x, y, r + 8, edge)
    for x, y, r in allc:
        c.circle(x, y, r, base)
    for x, y, r in allc:
        c.circle(x - r * 0.25, y - r * 0.3, r * 0.45, hi)
    c.save("clown_wig")


def flower(c, cx, cy, r, petal, center):
    for i in range(6):
        a = math.radians(i * 60)
        c.ell(cx + math.cos(a) * r * 0.85, cy + math.sin(a) * r * 0.85, r * 0.62, r * 0.45, petal,
              ang=math.degrees(a), outline=(0, 0, 0, 40), width=2)
    c.circle(cx, cy, r * 0.45, center, (200, 140, 20, 255), 3)


def flower_crown():
    c = C(1100, 340)
    arc = lambda t: (70 + 960 * t, 250 - 150 * math.sin(math.pi * t))
    c.line([arc(i / 40) for i in range(41)], (60, 140, 70, 255), 20)
    for i in range(8):
        x, y = arc((i + 0.5) / 8)
        c.ell(x, y + 4, 48, 20, (70, 165, 80, 255), ang=40 if i % 2 else -40)
    cols = [((255, 130, 175, 255)), ((255, 255, 255, 255)), ((255, 200, 60, 255)), ((190, 150, 255, 255))]
    for i in range(9):
        x, y = arc(i / 8)
        flower(c, x, y, 46 if i % 2 == 0 else 38, cols[i % 4], (255, 220, 70, 255))
    c.save("flower_crown")


def eyepatch():
    c = C(960, 520)
    c.line([(0, 30), (480, 260)], (20, 20, 24, 255), 16)
    c.line([(480, 260), (960, 490)], (20, 20, 24, 255), 16)
    c.ell(480, 260, 125, 100, (24, 24, 30, 255), ang=-20, outline=(70, 70, 80, 255), width=6)
    c.ell(445, 225, 40, 16, (255, 255, 255, 50), ang=-30)
    c.save("eyepatch")


def pirate_hat():
    c = C(1300, 720)
    body = [(40, 560), (180, 360), (470, 200), (650, 110), (830, 200), (1120, 360), (1260, 560),
            (1000, 500), (650, 470), (300, 500)]
    c.poly(body, (28, 28, 34, 255), (70, 70, 80, 255), 10)
    c.line([(150, 520), (650, 440), (1150, 520)], (230, 180, 50, 255), 14)
    c.circle(650, 300, 62, WHITE, (170, 170, 180, 255), 5)
    c.ell(628, 295, 14, 18, DARK)
    c.ell(672, 295, 14, 18, DARK)
    c.rrect(620, 335, 680, 372, 8, WHITE, (170, 170, 180, 255), 4)
    c.line([(560, 400), (740, 330)], WHITE, 16)
    c.line([(560, 330), (740, 400)], WHITE, 16)
    c.save("pirate_hat")


def santa_hat():
    c = C(900, 800)
    red = (210, 30, 42, 255)
    body = [(90, 650), (150, 430), (330, 230), (540, 150), (740, 175), (810, 260), (780, 400), (830, 650)]
    c.poly(body, red, (130, 10, 20, 255), 8)
    c.poly([(560, 170), (740, 190), (800, 270), (770, 420), (830, 650), (700, 650), (700, 380), (640, 240)],
           (170, 20, 32, 120))
    c.rrect(30, 600, 850, 770, 78, (250, 250, 252, 255), (200, 205, 215, 255), 6)
    c.circle(805, 215, 72, (250, 250, 252, 255), (200, 205, 215, 255), 6)
    c.save("santa_hat")


def santa_beard():
    c = C(1000, 900)
    circles = [(500, 640, 260), (300, 560, 190), (700, 560, 190), (380, 740, 170), (620, 740, 170),
               (500, 780, 150), (250, 380, 130), (750, 380, 130), (380, 250, 120), (620, 250, 120),
               (500, 230, 110)]
    for x, y, r in circles:
        c.circle(x, y, r + 9, (200, 206, 218, 255))
    for x, y, r in circles:
        c.circle(x, y, r, (251, 251, 253, 255))
    c.ell(500, 395, 128, 60, CLEAR)
    c.save("santa_beard")


def cowboy_hat():
    c = C(1500, 720)
    brown, dbrown, edge = (150, 98, 54, 255), (105, 66, 34, 255), (70, 42, 20, 255)
    c.ell(750, 520, 720, 110, dbrown, outline=edge, width=8)
    crown_pts = [(420, 480), (450, 190), (580, 100), (720, 170), (880, 100), (1020, 190), (1080, 480)]
    c.poly(crown_pts, brown, edge, 8)
    c.poly([(425, 410), (1075, 410), (1080, 490), (420, 490)], (60, 36, 20, 255))
    c.rrect(700, 420, 800, 480, 8, (230, 190, 70, 255), (150, 110, 20, 255), 4)
    c.save("cowboy_hat")


def rainbow():
    c = C(380, 1000)
    cols = [(236, 40, 50), (255, 140, 20), (255, 220, 40), (70, 200, 80), (50, 150, 240), (110, 80, 220),
            (190, 80, 200)]
    for k, col in enumerate(cols):
        pts = []
        for y in range(0, 1000, 10):
            spread = 0.35 + 0.65 * (y / 1000)
            x = 190 + (k - 3) * 38 * spread + 26 * math.sin(y / 70.0)
            pts.append((x, y))
        c.line(pts, col + (255,), 40)
    im = c.im
    px = im.load()
    for y in range(im.height):  # memudar ke bawah
        k = max(0.0, 1.0 - max(0, y - im.height * 0.55) / (im.height * 0.45))
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a:
                px[x, y] = (r, g, b, int(a * k))
    c.save("rainbow")


def sparkle():
    c = C(260, 260)
    c.poly([(130, 0), (160, 100), (260, 130), (160, 160), (130, 260), (100, 160), (0, 130), (100, 100)],
           (255, 250, 200, 255))
    c.glow(10)
    c.save("sparkle")


ASSETS = [dog_ears, dog_nose, dog_tongue, cat_ears, cat_whiskers, bunny_ears, bunny_nose, bunny_teeth,
          glasses_round, sunglasses, crown, party_hat, mustache, heart, devil_horns, halo, clown_nose,
          clown_wig, flower_crown, eyepatch, pirate_hat, santa_hat, santa_beard, cowboy_hat, rainbow, sparkle]



# ----------------------------------------------------------------------------------------
# Aset tambahan: stiker beranimasi (bingkai), karakter, dan tekstur
# ----------------------------------------------------------------------------------------
def flame_frame(k, n=6):
    c = C(500, 700)
    ph = 2 * math.pi * k / n
    base = 690

    def tongue(cx, w, h, wob, col):
        left = bezier((cx - w / 2, base), (cx - w * 0.68, base - h * 0.35), (cx - w * 0.15 + wob * 0.5, base - h * 0.68), (cx + wob, base - h))
        right = bezier((cx + wob, base - h), (cx + w * 0.15 + wob * 0.5, base - h * 0.68), (cx + w * 0.68, base - h * 0.35), (cx + w / 2, base))
        c.poly(left + right, col)

    tongue(250, 400, 560 + 40 * math.sin(ph), 22 * math.sin(ph + 1), (255, 70, 15, 255))
    tongue(125, 220, 380 + 50 * math.sin(ph + 2), 18 * math.sin(ph + 3), (255, 95, 20, 255))
    tongue(380, 220, 400 + 50 * math.sin(ph + 4), 18 * math.sin(ph + 5), (255, 95, 20, 255))
    tongue(250, 300, 430 + 35 * math.sin(ph + 1.5), 16 * math.sin(ph + 2.5), (255, 150, 25, 255))
    tongue(250, 190, 290 + 30 * math.sin(ph + 3.5), 10 * math.sin(ph), (255, 225, 90, 255))
    tongue(250, 90, 170 + 20 * math.sin(ph + 1), 6 * math.sin(ph + 2), (255, 250, 200, 255))
    c.glow(10)
    c.save("flame_%d" % k)


def flames():
    for k in range(6):
        flame_frame(k)


def bat_frame(k):
    c = C(520, 320)
    col, edge = (38, 30, 48, 255), (90, 70, 110, 255)
    up = k == 0
    for s in (-1, 1):
        def X(x):
            return 260 + s * x
        if up:
            pts = [(X(20), 170), (X(70), 90), (X(150), 30), (X(240), 20), (X(215), 70), (X(190), 80),
                   (X(170), 130), (X(135), 105), (X(105), 160), (X(70), 150), (X(45), 195)]
        else:
            pts = [(X(20), 170), (X(70), 200), (X(150), 250), (X(240), 290), (X(215), 245), (X(190), 235),
                   (X(170), 195), (X(135), 215), (X(105), 175), (X(70), 185), (X(45), 160)]
        c.poly(pts, col, edge, 5)
    c.ell(260, 170, 36, 54, col, outline=edge, width=5)
    c.circle(260, 118, 30, col, edge, 5)
    c.poly([(238, 100), (230, 50), (256, 90)], col, edge, 4)
    c.poly([(282, 100), (290, 50), (264, 90)], col, edge, 4)
    c.circle(249, 116, 6, (255, 70, 70, 255))
    c.circle(271, 116, 6, (255, 70, 70, 255))
    c.save("bat_%d" % k)


def butterfly_frame(k):
    c = C(520, 420)
    sx = 1.0 if k == 0 else 0.55
    for s in (-1, 1):
        def X(x):
            return 260 + s * x * sx
        c.ell(X(125), 130, 120 * sx, 100, (255, 120, 190, 255), ang=-s * 25, outline=(150, 40, 110, 255), width=6)
        c.ell(X(95), 285, 85 * sx, 85, (170, 110, 255, 255), ang=s * 20, outline=(90, 50, 170, 255), width=6)
        c.circle(X(125), 120, 22 * max(sx, .6), (255, 255, 255, 220))
        c.circle(X(90), 285, 16 * max(sx, .6), (255, 255, 255, 220))
    c.ell(260, 210, 14, 120, (50, 30, 50, 255))
    c.line([(255, 100), (225, 30)], (50, 30, 50, 255), 6)
    c.line([(265, 100), (295, 30)], (50, 30, 50, 255), 6)
    c.save("butterfly_%d" % k)


def butterflies_and_bats():
    for k in (0, 1):
        bat_frame(k)
        butterfly_frame(k)


def star():
    c = C(260, 260)
    pts = []
    for i in range(10):
        r = 120 if i % 2 == 0 else 52
        a = -math.pi / 2 + i * math.pi / 5
        pts.append((130 + r * math.cos(a), 135 + r * math.sin(a)))
    c.poly(pts, (255, 215, 40, 255), (230, 140, 10, 255), 8)
    c.save("star")


def pinocchio_nose():
    c = C(300, 640)
    c.poly([(70, 20), (230, 20), (178, 560), (150, 615), (122, 560)], (232, 150, 108, 255), (160, 92, 62, 255), 8)
    c.poly([(105, 40), (140, 40), (150, 560), (140, 590)], (255, 205, 165, 150))
    c.circle(150, 600, 24, (225, 120, 85, 255), (160, 92, 62, 255), 6)
    c.ell(150, 40, 80, 24, (240, 165, 125, 255), outline=(160, 92, 62, 255), width=6)
    c.save("pinocchio_nose")


def fangs():
    c = C(400, 220)
    for x in (95, 305):
        c.poly([(x - 36, 0), (x + 36, 0), (x + 6, 170), (x, 190), (x - 6, 170)], WHITE, (170, 170, 185, 255), 6)
    c.save("fangs")


def visor():
    c = C(1100, 330)
    neon = (0, 245, 255, 255)
    c.poly([(60, 60), (1040, 60), (1010, 250), (850, 300), (640, 250), (550, 230), (460, 250), (250, 300), (90, 250)],
           (8, 22, 48, 205))
    c.line([(60, 60), (1040, 60), (1010, 250), (850, 300), (640, 250), (550, 230), (460, 250), (250, 300), (90, 250), (60, 60)],
           neon, 14)
    c.line([(140, 105), (470, 105)], (140, 255, 255, 130), 10)
    c.line([(630, 105), (960, 105)], (140, 255, 255, 130), 10)
    c.line([(40, 80), (0, 70)], neon, 14)
    c.line([(1060, 80), (1100, 70)], neon, 14)
    c.glow(9)
    c.save("visor")


def ninja_band():
    c = C(1100, 420)
    c.rrect(40, 130, 1000, 270, 40, (24, 28, 52, 255), (70, 80, 130, 255), 6)
    c.rrect(430, 118, 640, 282, 18, (200, 205, 215, 255), (110, 115, 130, 255), 7)
    c.line([(470, 150), (600, 250)], (90, 95, 110, 255), 14)
    c.line([(600, 150), (470, 250)], (90, 95, 110, 255), 14)
    c.poly([(960, 200), (1080, 330), (1040, 380), (930, 280)], (24, 28, 52, 255), (70, 80, 130, 255), 6)
    c.poly([(940, 230), (1020, 270), (1000, 330), (920, 290)], (34, 40, 74, 255), (70, 80, 130, 255), 6)
    c.save("ninja_band")


def ninja_mask():
    c = C(1000, 700)
    col, edge = (26, 32, 56, 255), (74, 84, 130, 255)
    top = bezier((90, 40), (350, -20), (650, -20), (910, 40))
    right = bezier((910, 40), (940, 330), (830, 560), (620, 660))
    bottom = bezier((620, 660), (540, 700), (460, 700), (380, 660))
    left = bezier((380, 660), (170, 560), (60, 330), (90, 40))
    c.poly(top + right + bottom + left, col, edge, 8)
    c.line([(150, 120), (500, 160), (850, 120)], (60, 70, 110, 170), 6)
    c.line([(90, 40), (0, 0)], edge, 8)
    c.line([(910, 40), (1000, 0)], edge, 8)
    c.save("ninja_mask")


def helmet():
    c = C(1500, 1500)
    c.circle(750, 750, 735, (238, 242, 250, 255), (120, 130, 150, 255), 14)
    c.circle(750, 750, 575, CLEAR)
    c.circle(750, 750, 575, (150, 205, 255, 38))
    c.circle(750, 750, 575, None, (95, 105, 130, 255), 14) if False else None
    c.line(bezier((300, 420), (360, 330), (470, 270), (580, 250)), (255, 255, 255, 150), 26)
    c.line(bezier((340, 520), (380, 470), (430, 430), (480, 410)), (255, 255, 255, 90), 14)
    c.ell(750, 1290, 330, 70, (200, 206, 220, 255), outline=(120, 130, 150, 255), width=8)
    c.save("helmet")


def ice_crown():
    c = C(800, 420)
    peaks = [(60, 250), (170, 120), (250, 260), (340, 40), (430, 250), (520, 90), (610, 260), (700, 150), (760, 250)]
    pts = [(40, 400)] + peaks + [(780, 400)]
    c.poly(pts, (150, 222, 255, 235), (50, 130, 215, 255), 8)
    for (x, y) in ((340, 40), (520, 90), (170, 120), (700, 150)):
        c.poly([(x, y + 10), (x - 22, y + 130), (x + 4, y + 120)], (255, 255, 255, 190))
    c.rrect(40, 330, 780, 410, 20, (110, 190, 245, 255), (50, 130, 215, 255), 6)
    c.glow(7)
    c.save("ice_crown")


def matrix_texture():
    from PIL import ImageFont
    import random
    W, H = 640, 1440
    im = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(im)
    try:
        font = ImageFont.load_default(size=16)
    except TypeError:
        font = ImageFont.load_default()
    rnd = random.Random(7)
    chars = "0123456789ABCDEFXYZ<>/\\|+*=#$%&@"
    col_w = 20
    for cx in range(0, W, col_w):
        length = rnd.randint(8, 30)
        start = rnd.randint(0, H // 18)
        for i in range(length):
            y = ((start + i) * 18) % H
            k = i / length
            g = int(60 + 195 * k)
            colr = (int(g * 0.1), g, int(g * 0.25)) if i < length - 1 else (200, 255, 210)
            d.text((cx, y), rnd.choice(chars), fill=colr, font=font)
    im.save(os.path.join(OUT, "matrix.png"))


ASSETS += [flames, butterflies_and_bats, star, pinocchio_nose, fangs, visor, ninja_band, ninja_mask, helmet,
           ice_crown, matrix_texture]

# ---------------------------------------------------------------------------------------------
# Katalog penempatan. Satu sumber untuk C# (ArCatalog.cs) dan pratinjau uji.
# (asset, anchor, lebar relatif lebar wajah, pivotX, pivotY, offsetX, offsetY, rotasi derajat, flag)
# Offset dalam satuan lebar wajah: X sepanjang garis mata, Y ke bawah (sumbu wajah).
# flag: m = hanya saat mulut terbuka, p = berdenyut, b = naik-turun, n = tanpa efek putar kepala (yaw)
# ---------------------------------------------------------------------------------------------
SPECS = {
    "Dog": [("dog_ears", "HeadTop", 1.50, .5, .30, 0, -.02, 0, ""),
            ("dog_nose", "Nose", .32, .5, .35, 0, 0, 0, ""),
            ("dog_tongue", "LowerLip", .26, .5, 0.0, 0, -.01, 0, "m")],
    "Cat": [("cat_ears", "HeadTop", 1.22, .5, .88, 0, .03, 0, ""),
            ("cat_whiskers", "Nose", 1.25, .5, .5, 0, .02, 0, "")],
    "Bunny": [("bunny_ears", "HeadTop", .85, .5, .96, 0, .05, 0, ""),
              ("bunny_nose", "Nose", .26, .5, .30, 0, 0, 0, ""),
              ("bunny_teeth", "Mouth", .15, .5, 0.0, 0, 0, 0, "")],
    "Nerd": [("glasses_round", "Eyes", 1.08, .5, .5, 0, 0, 0, "")],
    "Shades": [("sunglasses", "Eyes", 1.10, .5, .5, 0, .01, 0, "")],
    "Crown": [("crown", "HeadTop", .80, .5, .98, 0, .02, 0, "")],
    "PartyHat": [("party_hat", "HeadTop", .52, .5, .96, .14, .04, 12, "")],
    "Mustache": [("mustache", "UpperLip", .62, .5, .55, 0, -.02, 0, "")],
    "HeartEyes": [("heart", "EyeA", .32, .5, .5, 0, 0, 0, "p"),
                  ("heart", "EyeB", .32, .5, .5, 0, 0, 0, "p")],
    "Devil": [("devil_horns", "HeadTop", .95, .5, .92, 0, .03, 0, "")],
    "Angel": [("halo", "HeadTop", .78, .5, .5, 0, -.26, 0, "b")],
    "Clown": [("clown_wig", "HeadTop", 1.70, .5, .62, 0, .14, 0, ""),
              ("clown_nose", "Nose", .24, .5, .5, 0, .0, 0, "")],
    "FlowerCrown": [("flower_crown", "HeadTop", 1.22, .5, .62, 0, .07, 0, "")],
    "Pirate": [("pirate_hat", "HeadTop", 1.35, .5, .78, 0, .04, 0, ""),
               ("eyepatch", "EyeB", 1.15, .5, .5, 0, 0, 0, "")],
    "Santa": [("santa_hat", "HeadTop", 1.22, .45, .88, 0, .05, 0, ""),
              ("santa_beard", "Mouth", 1.05, .5, .42, 0, .04, 0, "")],
    "Cowboy": [("cowboy_hat", "HeadTop", 1.60, .5, .62, 0, .04, 0, ""),
               ("mustache", "UpperLip", .62, .5, .55, 0, -.02, 0, "")],
    "Rainbow": [("rainbow", "LowerLip", .46, .5, 0.0, 0, -.01, 0, "m")],
    "Sparkle": [("sparkle", "CheekA", .20, .5, .5, -.06, -.10, 0, "p"),
                ("sparkle", "CheekB", .17, .5, .5, .06, -.14, 0, "p"),
                ("sparkle", "HeadTop", .15, .5, .5, .22, .02, 0, "p")],
}



SPECS.update({
    "Fire": [("flame", "HeadTop", 1.15, .5, .93, 0, .03, 0, "", dict(Frames=6, Fps=14.0))],
    "Bats": [("bat", "Eyes", .34, .5, .5, 0, -.05, 0, "", dict(Frames=2, Fps=10.0, OrbitR=.80, OrbitSpeed=1.7, OrbitPhase=0.0, OrbitTilt=.32)),
             ("bat", "Eyes", .30, .5, .5, 0, -.05, 0, "", dict(Frames=2, Fps=9.0, Phase=.5, OrbitR=.95, OrbitSpeed=-1.4, OrbitPhase=2.1, OrbitTilt=.30)),
             ("bat", "Eyes", .27, .5, .5, 0, -.05, 0, "", dict(Frames=2, Fps=11.0, OrbitR=.70, OrbitSpeed=2.0, OrbitPhase=4.2, OrbitTilt=.36))],
    "Butterflies": [("butterfly", "Eyes", .30, .5, .5, 0, -.10, 0, "", dict(Frames=2, Fps=6.0, OrbitR=.75, OrbitSpeed=1.1, OrbitPhase=0.0, OrbitTilt=.35)),
                    ("butterfly", "Eyes", .26, .5, .5, 0, -.10, 0, "", dict(Frames=2, Fps=7.0, Phase=.5, OrbitR=.90, OrbitSpeed=-0.9, OrbitPhase=2.4, OrbitTilt=.30)),
                    ("butterfly", "Eyes", .22, .5, .5, 0, -.10, 0, "", dict(Frames=2, Fps=5.0, OrbitR=.65, OrbitSpeed=1.4, OrbitPhase=4.4, OrbitTilt=.38))],
    "LoveRain": [("heart", "HeadTop", .20, .5, .5, ox, -.05, 0, "", dict(DriftY=1.1, DriftPeriod=2.4, Phase=ph))
                 for ox, ph in ((-.34, 0.0), (-.12, .17), (.12, .33), (.34, .5), (-.22, .66), (.22, .83))],
    "Dizzy": [("star", "HeadTop", .19, .5, .5, 0, -.04, 0, "", dict(OrbitR=.46, OrbitSpeed=3.2, OrbitPhase=k * math.pi / 2, OrbitTilt=.22))
              for k in range(4)],
    "Pinocchio": [("pinocchio_nose", "Nose", .30, .5, .04, 0, -.01, 0, "")],
    "Frozen": [("ice_crown", "HeadTop", .95, .5, .95, 0, .03, 0, ""),
               ("sparkle", "CheekA", .20, .5, .5, -.05, -.08, 0, "p"),
               ("sparkle", "CheekB", .17, .5, .5, .05, -.12, 0, "p"),
               ("sparkle", "Eyes", .14, .5, .5, .0, -.35, 0, "p")],
    "Vampire": [("fangs", "UpperLip", .46, .5, 0.0, 0, .012, 0, "")],
    "FireBreath": [("flame", "LowerLip", .95, .5, .93, 0, .02, 180, "m", dict(Frames=6, Fps=16.0))],
    "Visor": [("visor", "Eyes", 1.12, .5, .5, 0, 0.0, 0, "")],
    "Ninja": [("ninja_mask", "Nose", 1.0, .5, .25, 0, 0.0, 0, ""),
              ("ninja_band", "HeadTop", 1.08, .5, .5, 0, .10, 0, "")],
    "Astronaut": [("helmet", "Eyes", 1.95, .5, .5, 0, .24, 0, "")],
})

def write_catalog():
    lines = ["// <auto-generated> oleh tools/make_stickers.py. Boleh diedit manual (angka penempatan).",
             "using System.Collections.Generic;", "", "namespace PhotoBooth;", "",
             "public static class ArCatalog", "{",
             "    public static readonly Dictionary<EffectKind, StickerSpec[]> Stickers = new()", "    {"]
    for k, items in SPECS.items():
        lines.append(f"        [EffectKind.{k}] = new StickerSpec[]")
        lines.append("        {")
        for it in items:
            (a, anc, w, px, py, ox, oy, rot, fl) = it[:9]
            ex = it[9] if len(it) > 9 else {}
            extra = ""
            for key, val in ex.items():
                extra += f", {key}: {val}f" if isinstance(val, float) else f", {key}: {val}"
            if "m" in fl: extra += ", MouthOnly: true"
            if "p" in fl: extra += ", Pulse: true"
            if "b" in fl: extra += ", Bob: 0.02f"
            if "n" in fl: extra += ", Yaw: false"
            lines.append(f'            new("{a}", Anchor.{anc}, {w}f, {px}f, {py}f, {ox}f, {oy}f, {rot}f{extra}),')
        lines.append("        },")
    lines += ["    };", "}", ""]
    with open(os.path.join(HERE, "..", "ArCatalog.cs"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


# ---------------------------------------------------------------------------------------------
# Pratinjau uji: wajah tiruan + rumus penempatan yang sama dengan ArEffects.cs
# ---------------------------------------------------------------------------------------------
def mock():
    import itertools
    cols = 6
    names = list(SPECS.keys())
    rows = math.ceil(len(names) / cols)
    CW, CH = 520, 620
    sheet = Image.new("RGB", (cols * CW, rows * CH), (70, 80, 95))
    for idx, name in enumerate(names):
        roll = math.radians(12 if idx % 2 else 0)
        base = Image.new("RGBA", (CW, CH), (70, 80, 95, 255))
        d = ImageDraw.Draw(base)
        cx, cy = 260, 330
        u = (math.cos(roll), math.sin(roll))
        v = (-u[1], u[0])
        W_ = 220.0

        def P(ox, oy):  # titik dalam koordinat wajah (satuan lebar)
            return (cx + (u[0] * ox + v[0] * oy) * W_, cy + (u[1] * ox + v[1] * oy) * W_)
        face = [P(math.cos(t) * .5, math.sin(t) * .62 + .05) for t in [i * 2 * math.pi / 60 for i in range(60)]]
        d.polygon(face, fill=(238, 196, 160, 255))
        for sx in (-1, 1):
            e = P(sx * .24, -.05)
            d.ellipse((e[0] - 16, e[1] - 8, e[0] + 16, e[1] + 8), fill=WHITE)
            d.ellipse((e[0] - 6, e[1] - 6, e[0] + 6, e[1] + 6), fill=(60, 40, 30, 255))
        n = P(0, .18)
        d.ellipse((n[0] - 9, n[1] - 9, n[0] + 9, n[1] + 9), fill=(215, 165, 130, 255))
        m = P(0, .40)
        d.ellipse((m[0] - 38, m[1] - 8, m[0] + 38, m[1] + 22), fill=(120, 30, 40, 255))  # mulut terbuka
        pts = {
            "HeadTop": P(0, -.50), "Eyes": P(0, -.05), "EyeA": P(-.24, -.05), "EyeB": P(.24, -.05),
            "Nose": P(0, .18), "Mouth": P(0, .40), "UpperLip": P(0, .34),
            "CheekA": P(-.30, .15), "CheekB": P(.30, .15), "LowerLip": P(0, .43),
        }
        for it in SPECS[name]:
            (a, anc, w, px, py, ox, oy, rot, fl) = it[:9]
            a = a + "_0" if os.path.exists(os.path.join(OUT, a + "_0.png")) else a
            im = Image.open(os.path.join(OUT, a + ".png")).convert("RGBA")
            s = (w * W_) / im.width
            ang = roll + math.radians(rot)
            ca, sa = math.cos(ang), math.sin(ang)
            ax, ay = pts[anc]
            ax += (u[0] * ox + v[0] * oy) * W_
            ay += (u[1] * ox + v[1] * oy) * W_
            A, B, Cc, D = ca * s, -sa * s, sa * s, ca * s
            ppx, ppy = px * im.width, py * im.height
            tx, ty = ax - (A * ppx + B * ppy), ay - (Cc * ppx + D * ppy)
            det = A * D - B * Cc
            ia, ib, ic, idd = D / det, -B / det, -Cc / det, A / det
            coef = (ia, ib, -(ia * tx + ib * ty), ic, idd, -(ic * tx + idd * ty))
            layer = im.transform((CW, CH), Image.AFFINE, coef, resample=Image.BICUBIC)
            base = Image.alpha_composite(base, layer)
        ImageDraw.Draw(base).text((8, 6), name, fill=WHITE)
        sheet.paste(base.convert("RGB"), ((idx % cols) * CW, (idx // cols) * CH))
    sheet = sheet.resize((sheet.width // 2, sheet.height // 2), Image.LANCZOS)
    sheet.save(os.path.join(HERE, "mock_preview.png"))


if __name__ == "__main__":
    for fn in ASSETS:
        fn()
    write_catalog()
    print("stiker:", len(ASSETS), "| efek stiker:", len(SPECS))
    if "--mock" in sys.argv:
        mock()
        print("mock_preview.png dibuat")
