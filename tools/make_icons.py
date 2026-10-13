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
