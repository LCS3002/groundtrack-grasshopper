"""The node lexicon: every component as Grasshopper draws it (tools/render_nodes.py renders each on
black and on white; the pair gives exact transparency, shadows included), each with its name and
a short note, in a wide panorama on a transparent background - with dark text (for light pages)
and with light text (for dark pages).

    python tools/render_nodes.py <render dir>          (in Rhino 8, headless)
    python tools/make_lexicon.py <render dir> <out dir>
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

COLUMNS = [[("Data", ["Field", "Transform Field", "Field from Curves"])],
           [("Flow", ["Streamlines", "Particles"])],
           [("", ["Merge Fields", "Blend Fields", "Smooth Field"])],
           [("Shape", ["Warp Field", "Landscape", "Loft Fields", "Bridge", "Field Surface"])],
           [("Forces", ["Force", "Axis Force", "Attract", "Curve Force"])],
           [("", ["Field Force", "Interact", "Obstacle", "Apply Forces"])],
           [("Analyse", ["Crossings", "Intersect Fields"]), ("Time", ["Timeline", "Replay"]), ("Colour", ["Gradient"])]]
NOTE = {
    "Field": "Loads a movement field from your results: site, clip, people or vehicles - or just one moment of it.",
    "Transform Field": "Turns, scales, stretches and mirrors a field in its own plane, then moves it: stack fields as layers.",
    "Field from Curves": "Draw your own flow: it runs along the curves you draw.",
    "Streamlines": "Evenly spaced flow lines through a field - flat, tilted or bent.",
    "Particles": "The simulation: particles born where people were, following the field, pushed by forces. Several fields are layers; Join carries particles from one onto the next.",
    "Merge Fields": "Several fields into one, without seams.",
    "Blend Fields": "Morphs from one field into another with a single slider.",
    "Smooth Field": "Relaxes a field: fills gaps, turns cliffs into ramps.",
    "Warp Field": "Bends a field into 3D - twist, bend, swirl, wave, taper, bulge. The flow follows the new shape; Grasshopper's own morphs work too.",
    "Landscape": "Shapes a field's ground: rolled into hills, raised where it is busiest, or draped over a surface you model.",
    "Loft Fields": "Joins stacked layers at their edges with rounded folds: one continuous landscape, the flow blending across.",
    "Bridge": "Swooping ribbons between fields - found where the flows leave and arrive, or along curves you draw.",
    "Field Surface": "A field's ground as a mesh coloured by speed, and its outlines as smooth curves.",
    "Force": "A constant push plus turbulence.",
    "Axis Force": "Swirls particles around an axis, sucks them in, lifts them.",
    "Attract": "Pulls particles to points - or pushes them away.",
    "Curve Force": "Particles follow, approach or avoid curves.",
    "Field Force": "One field's flow as a wind on another layer, pulling it to that field's ground.",
    "Interact": "Particles keep apart, gather and align - layers weave through each other.",
    "Obstacle": "Buildings and massing that particles flow around.",
    "Apply Forces": "Bends the field's flow itself with forces: swirls, pulls, detours.",
    "Crossings": "Where two flows cross, at what angle - and a heat map.",
    "Intersect Fields": "Where layers cut through each other, like intersecting planes.",
    "Timeline": "The clock: play, scrub, loop - and record the viewport as frames.",
    "Replay": "The real people and vehicles at that moment, with their trails.",
    "Gradient": "Recolours everything: presets or your own colours.",
}

NODE_SCALE = 0.42            # of the 4x renders: about 1.7x Grasshopper's own size
TEXT_W, COL_GAP, ROW_GAP, MARGIN, HEAD = 250, 64, 34, 80, 54


def font(name, size):
    return ImageFont.truetype(str(Path("C:/Windows/Fonts") / name), size)


F_NAME, F_NOTE, F_HEAD, F_TITLE = font("seguisb.ttf", 18), font("segoeui.ttf", 14), font("seguisb.ttf", 14), font("segoeuil.ttf", 34)


def matte(black_path, white_path):
    """RGBA from the same picture on black and on white."""
    b = np.asarray(Image.open(black_path).convert("RGB"), dtype=np.float64)
    w = np.asarray(Image.open(white_path).convert("RGB"), dtype=np.float64)
    a = np.clip(1.0 - (w - b).mean(axis=2) / 255.0, 0, 1)
    rgb = np.where(a[..., None] > 1e-3, np.clip(b / np.maximum(a[..., None], 1e-3), 0, 255), 0)
    return Image.fromarray(np.dstack([rgb, a * 255]).astype(np.uint8))


def wrap(text, width_px):
    lines, cur = [], ""
    for wd in text.split():
        t = (cur + " " + wd).strip()
        if F_NOTE.getlength(t) > width_px and cur:
            lines.append(cur)
            cur = wd
        else:
            cur = t
    if cur:
        lines.append(cur)
    return lines


def render(nodes, out, ink, faint):
    node_w = max(im.width for im in nodes.values())
    col_w = node_w + 22 + TEXT_W

    def entry_h(name):
        return max(nodes[name].height, 34 + 20 * len(wrap(NOTE[name], TEXT_W))) + ROW_GAP

    heights = [sum(HEAD + sum(entry_h(n) for n in names) for _, names in col) for col in COLUMNS]
    W = 2 * MARGIN + len(COLUMNS) * col_w + (len(COLUMNS) - 1) * COL_GAP
    H = MARGIN + 80 + max(heights) + MARGIN - ROW_GAP
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.text((MARGIN, MARGIN - 14), "groundtrack for Grasshopper", font=F_TITLE, fill=ink)
    for ci, col in enumerate(COLUMNS):
        x = MARGIN + ci * (col_w + COL_GAP)
        y = MARGIN + 80
        for label, names in col:
            if label:
                d.text((x, y), label.upper(), font=F_HEAD, fill=faint)
            d.line([(x, y + 26), (x + col_w, y + 26)], fill=faint, width=1)
            y += HEAD
            for n in names:
                node = nodes[n]
                im.alpha_composite(node, (x + (node_w - node.width) // 2, y))
                tx = x + node_w + 22
                d.text((tx, y + 6), n, font=F_NAME, fill=ink)
                for k, line in enumerate(wrap(NOTE[n], TEXT_W)):
                    d.text((tx, y + 36 + 20 * k), line, font=F_NOTE, fill=ink)
                y += entry_h(n)
    im.save(out)
    return im.size


if __name__ == "__main__":
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    meta = json.load(open(src / "meta.json", encoding="utf-8"))
    nodes = {}
    for m in meta:
        rgba = matte(src / f"{m['name']}_black.png", src / f"{m['name']}_white.png")
        nodes[m["name"]] = rgba.resize((round(rgba.width * NODE_SCALE), round(rgba.height * NODE_SCALE)), Image.LANCZOS)
    print(render(nodes, out / "groundtrack_lexicon_dark.png", (34, 34, 38, 255), (34, 34, 38, 120)))
    print(render(nodes, out / "groundtrack_lexicon_light.png", (245, 245, 242, 255), (245, 245, 242, 130)))
