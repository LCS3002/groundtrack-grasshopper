"""Draws the 24 x 24 component icons (supersampled) into src/Groundtrack/Icons/."""
import math
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parents[1] / "src" / "Groundtrack" / "Icons"
S = 4                      # supersampling
RAMP = [(0.0, (69, 89, 203)), (0.2, (32, 199, 223)), (0.4, (109, 253, 98)),
        (0.6, (231, 215, 57)), (0.8, (245, 105, 24)), (1.0, (161, 18, 1))]
INK = (40, 40, 44, 255)


def ramp(t):
    t = max(0.0, min(1.0, t))
    for (a, ca), (b, cb) in zip(RAMP, RAMP[1:]):
        if t <= b:
            f = (t - a) / (b - a)
            return tuple(int(ca[k] + (cb[k] - ca[k]) * f) for k in range(3)) + (255,)
    return RAMP[-1][1] + (255,)


def canvas():
    im = Image.new("RGBA", (24 * S, 24 * S), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)


def save(im, name):
    im.resize((24, 24), Image.LANCZOS).save(OUT / f"{name}.png")


def arrow(d, x0, y0, x1, y1, c, w):
    d.line([(x0 * S, y0 * S), (x1 * S, y1 * S)], fill=c, width=w * S)
    a = math.atan2(y1 - y0, x1 - x0)
    for s in (2.5, -2.5):
        d.line([(x1 * S, y1 * S), ((x1 - 3 * math.cos(a + s * 0.25)) * S, (y1 - 3 * math.sin(a + s * 0.25)) * S)],
               fill=c, width=w * S)


OUT.mkdir(parents=True, exist_ok=True)

im, d = canvas()                                   # field: a grid of arrows, slow -> fast
for j, y in enumerate((5, 12, 19)):
    for i, x in enumerate((4, 11, 18)):
        a = 0.35 * (i - 1) + 0.25 * (j - 1)
        arrow(d, x, y, x + 4.5 * math.cos(a), y - 4.5 * math.sin(a), ramp((i + j) / 4), 1)
save(im, "field")

im, d = canvas()                                   # streamlines: three flowing lines
for k, y in enumerate((6, 12, 18)):
    pts = [(x * S, (y + 2.6 * math.sin(x / 3.4 + k)) * S) for x in [1 + 0.5 * n for n in range(45)]]
    d.line(pts, fill=ramp(0.15 + 0.35 * k), width=2 * S, joint="curve")
save(im, "streamlines")

im, d = canvas()                                   # particles: dots with tails
for k, (x, y) in enumerate([(18, 6), (12, 12), (19, 17), (7, 18), (6, 7)]):
    c = ramp(0.2 + 0.18 * k)
    d.line([((x - 6) * S, (y + 2) * S), (x * S, y * S)], fill=c[:3] + (120,), width=1 * S)
    d.ellipse([(x - 2) * S, (y - 2) * S, (x + 2) * S, (y + 2) * S], fill=c)
save(im, "particles")

im, d = canvas()                                   # blend: two fields overlapping
d.ellipse([2 * S, 4 * S, 16 * S, 18 * S], fill=ramp(0.1)[:3] + (190,))
d.ellipse([8 * S, 6 * S, 22 * S, 20 * S], fill=ramp(0.85)[:3] + (190,))
save(im, "blend")

im, d = canvas()                                   # crossings: two flows and the hot spot
d.line([(2 * S, 20 * S), (22 * S, 4 * S)], fill=ramp(0.1), width=2 * S)
d.line([(2 * S, 5 * S), (22 * S, 19 * S)], fill=ramp(0.9), width=2 * S)
d.ellipse([8 * S, 8 * S, 16 * S, 16 * S], fill=(252, 220, 80, 255), outline=INK, width=S)
save(im, "crossings")

im, d = canvas()                                   # timeline: play + bar
d.polygon([(4 * S, 3 * S), (4 * S, 13 * S), (12 * S, 8 * S)], fill=INK)
d.rectangle([2 * S, 17 * S, 22 * S, 19 * S], fill=(160, 160, 166, 255))
d.rectangle([2 * S, 17 * S, 13 * S, 19 * S], fill=ramp(0.75))
d.ellipse([11 * S, 15 * S, 15 * S, 21 * S], fill=INK)
save(im, "timeline")

im, d = canvas()                                   # replay: a track and a person-dot on it
pts = [(2, 20), (6, 15), (11, 14), (15, 9), (20, 5)]
for (x0, y0), (x1, y1), t in zip(pts, pts[1:], (0.1, 0.35, 0.6, 0.85)):
    d.line([(x0 * S, y0 * S), (x1 * S, y1 * S)], fill=ramp(t), width=2 * S)
d.ellipse([12 * S, 7 * S, 18 * S, 13 * S], fill=INK)
save(im, "replay")
print(sorted(p.name for p in OUT.iterdir()))

# ---- forces, merge, curve field, gradient (0.2)
im, d = canvas()                                   # force: arrows pushing one way + wiggle
for k, y in enumerate((6, 12, 18)):
    arrow(d, 3, y, 19, y - 2, ramp(0.2 + 0.3 * k), 2)
save(im, "force")

im, d = canvas()                                   # axis force: axis + swirl
d.line([(12 * S, 2 * S), (12 * S, 22 * S)], fill=INK, width=2 * S)
d.arc([3 * S, 7 * S, 21 * S, 17 * S], 200, 520, fill=ramp(0.15), width=2 * S)
arrow(d, 19, 9, 20, 10, ramp(0.15), 2)
save(im, "axis")

im, d = canvas()                                   # attract: arrows into a point
for a in range(0, 360, 60):
    x, y = 12 + 10 * math.cos(math.radians(a)), 12 + 10 * math.sin(math.radians(a))
    arrow(d, x, y, 12 + 4.5 * math.cos(math.radians(a)), 12 + 4.5 * math.sin(math.radians(a)), ramp(a / 360), 1)
d.ellipse([9 * S, 9 * S, 15 * S, 15 * S], fill=INK)
save(im, "attract")

im, d = canvas()                                   # curve force: a curve + arrows along it
pts = [(x * S, (12 + 6 * math.sin(x / 4)) * S) for x in [1 + 0.5 * n for n in range(45)]]
d.line(pts, fill=INK, width=2 * S, joint="curve")
for x in (5, 12, 19):
    y = 12 + 6 * math.sin(x / 4)
    arrow(d, x, y - 4, x + 3, y - 4 + 6 * math.cos(x / 4) * 0.75, ramp(0.8), 1)
save(im, "curveforce")

im, d = canvas()                                   # field force: field arrows bending a path
for j, y in enumerate((5, 12, 19)):
    for i, x in enumerate((4, 12)):
        arrow(d, x, y, x + 4, y - 2, ramp(0.3 + 0.2 * i), 1)
d.line([(2 * S, 22 * S), (10 * S, 16 * S), (22 * S, 8 * S)], fill=ramp(0.9), width=2 * S)
save(im, "fieldforce")

im, d = canvas()                                   # interact: two strands intertwining
for k, c in enumerate((ramp(0.1), (255, 60, 190, 255))):
    pts = [(x * S, (12 + 6 * math.sin(x / 3 + k * math.pi)) * S) for x in [1 + 0.5 * n for n in range(45)]]
    d.line(pts, fill=c, width=2 * S, joint="curve")
save(im, "interact")

im, d = canvas()                                   # obstacle: block + flow around
d.rectangle([9 * S, 8 * S, 15 * S, 16 * S], fill=(120, 120, 128, 255))
for y, bend in ((5, -1), (12, 0), (19, 1)):
    pts = [(x * S, (y + (bend * 3 if 7 <= x <= 17 else 0) - (4 if y == 12 and 6 <= x <= 18 else 0)) * S) for x in range(1, 24)]
    d.line(pts, fill=ramp(0.3 + 0.2 * (y / 19)), width=S + 2)
save(im, "obstacle")

im, d = canvas()                                   # merge: three fields into one
for k, c in enumerate((ramp(0.1), ramp(0.5), ramp(0.9))):
    d.ellipse([(2 + 4 * k) * S, (2 + 3 * k) * S, (12 + 4 * k) * S, (12 + 3 * k) * S], fill=c[:3] + (170,))
arrow(d, 15, 17, 21, 21, INK, 2)
save(im, "merge")

im, d = canvas()                                   # curve field: a drawn curve with arrows beside it
pts = [(x * S, (14 - 5 * math.sin(x / 5)) * S) for x in [1 + 0.5 * n for n in range(45)]]
d.line(pts, fill=INK, width=S + 1, joint="curve")
for x in (4, 10, 16):
    for off in (-5, 5):
        y = 14 - 5 * math.sin(x / 5) + off
        arrow(d, x, y, x + 4, y - 5 * (math.sin((x + 4) / 5) - math.sin(x / 5)), ramp(0.6), 1)
save(im, "curvefield")

im, d = canvas()                                   # gradient: a colour bar
for x in range(2, 22):
    d.rectangle([x * S, 6 * S, (x + 1) * S, 18 * S], fill=ramp((x - 2) / 19))
d.rectangle([2 * S, 6 * S, 22 * S, 18 * S], outline=INK, width=S)
save(im, "gradient")
print("icons:", len(list(OUT.iterdir())))

# ---- transform field, smooth field (0.2)
im, d = canvas()                                   # transform field: a field square turned, with a turn arrow
d.polygon([(12 * S, 3 * S), (21 * S, 12 * S), (12 * S, 21 * S), (3 * S, 12 * S)], outline=INK, width=S + 1)
for x0, y0 in ((8, 12), (12, 8), (12, 16), (16, 12)):
    arrow(d, x0 - 1.5, y0 + 1.5, x0 + 1.5, y0 - 1.5, ramp(0.2 + 0.03 * x0), 1)
d.arc([1 * S, 1 * S, 23 * S, 23 * S], 290, 350, fill=(255, 60, 190, 255), width=2 * S)
save(im, "transformfield")

im, d = canvas()                                   # smooth: jagged line -> smooth line
d.line([(2 * S, 8 * S), (6 * S, 3 * S), (10 * S, 9 * S), (14 * S, 3 * S), (18 * S, 9 * S), (22 * S, 4 * S)], fill=(150, 150, 155, 255), width=S + 1)
pts = [(x * S, (17 + 3 * math.sin(x / 3.2)) * S) for x in [2 + 0.5 * n for n in range(41)]]
d.line(pts, fill=ramp(0.15), width=2 * S, joint="curve")
save(im, "smooth")
print("icons:", len(list(OUT.iterdir())))
