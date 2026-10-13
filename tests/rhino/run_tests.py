"""Tests the plugin in real Rhino 8 + Grasshopper, headless (Rhino.Inside), on real data.

    python run_tests.py <results folder> [--fw net8.0|net48] [--example out.gh]

The .gha must be installed in %APPDATA%\\Grasshopper\\Libraries\\groundtrack (see tools/install.py).
Every check prints its numbers; the run ends with PASS or FAIL. The data stay local: nothing of
the results folder is written into this repository.
"""
import argparse
import csv
import math
import os
import re
import statistics
import sys
import time

ap = argparse.ArgumentParser()
ap.add_argument("results")
ap.add_argument("--fw", default="net8.0")
ap.add_argument("--example", default="")
args = ap.parse_args()
os.environ["GROUNDTRACK_RESULTS"] = args.results

import rhinoinside  # noqa: E402

rhinoinside.load(r"C:\Program Files\Rhino 8\System", args.fw)
import clr  # noqa: E402
import System  # noqa: E402
import Rhino  # noqa: E402

gh_plugin = Rhino.RhinoApp.GetPlugInObject("Grasshopper")
gh_plugin.RunHeadless()
clr.AddReference(r"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll")
clr.AddReference("System.Drawing")
import Grasshopper  # noqa: E402
from Grasshopper.Kernel import GH_Document, GH_DocumentIO  # noqa: E402
from Grasshopper.Kernel.Special import GH_ValueList, GH_ValueListItem, GH_ValueListMode, GH_Group, GH_Panel  # noqa: E402
from Grasshopper.Kernel.Types import GH_String, GH_Number, GH_Boolean, GH_Integer  # noqa: E402
from System.Drawing import PointF, Color  # noqa: E402

FAIL = []
G = {"field": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e501", "stream": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e502",
     "particles": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e503", "blend": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e504",
     "cross": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e505", "timeline": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e506",
     "replay": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e507", "merge": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e508",
     "curvefield": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e509", "gradient": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e510",
     "force": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e511", "axis": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e512",
     "attract": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e513", "curveforce": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e514",
     "fieldforce": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e515", "interact": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e516",
     "obstacle": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e517"}


def check(name, ok, detail):
    print(f"  [{'ok' if ok else 'FAIL'}] {name}: {detail}", flush=True)
    if not ok:
        FAIL.append(name)


cs = Grasshopper.Instances.ComponentServer
ours = [p for p in cs.ObjectProxies if str(p.Desc.Category) == "groundtrack"]
print(f"Rhino {Rhino.RhinoApp.Version} ({args.fw}), Grasshopper headless; groundtrack components: "
      f"{sorted(str(p.Desc.Name) for p in ours)}", flush=True)
check("plugin loaded", len(ours) >= 19, f"{len(ours)} components on the groundtrack tab")
asm = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == "groundtrack")
clr.AddReference(asm.Location)
from Groundtrack.Core import ParticleSystem, Streamlines, Catalog  # noqa: E402

doc = GH_Document()
doc.Enabled = True


def impl(o):
    """pythonnet returns interface types: the concrete object behind them."""
    return getattr(o, "__implementation__", o)


def emit(key, x=0, y=0):
    obj = impl(cs.EmitObject(System.Guid(G[key])))
    obj.CreateAttributes()
    obj.Attributes.Pivot = PointF(x, y)
    if key == "field":   # no automatic dropdowns: the test sets the inputs itself
        f = obj.GetType().GetField("_read", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        f.SetValue(obj, True)
    doc.AddObject(obj, False)
    return obj


def put(comp, ix, value):
    p = impl(comp.Params.Input[ix])
    p.PersistentData.Clear()
    goo = {str: GH_String, float: GH_Number, bool: GH_Boolean, int: GH_Integer}[type(value)](value)
    p.PersistentData.Append(goo)
    p.ExpireSolution(False)


def wire(src_comp, out_ix, dst_comp, in_ix):
    impl(dst_comp.Params.Input[in_ix]).AddSource(impl(src_comp.Params.Output[out_ix]))


def out(comp, ix):
    return [impl(g) for g in impl(comp.Params.Output[ix]).VolatileData.AllData(True)]


def solve():
    doc.NewSolution(False)


def msgs(comp):
    return [str(m) for m in comp.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)] + \
           [str(m) for m in comp.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning)]


def field_comp(site, clip, group, x=0, y=0):
    c = emit("field", x, y)
    put(c, 1, site)
    put(c, 2, clip)
    put(c, 3, group)
    return c


def angle(ax, ay, bx, by):
    na, nb = math.hypot(ax, ay), math.hypot(bx, by)
    if na < 1e-12 or nb < 1e-12:
        return 0.0
    return math.degrees(math.acos(max(-1.0, min(1.0, (ax * bx + ay * by) / (na * nb)))))


SITES = [("reuters_plaza", "people"), ("blackwall_motorway", "vehicles"), ("trafalgar_square", "people")]
fields = {}
for k, (site, group) in enumerate(SITES):
    print(f"\n== {site} (whole, {group})", flush=True)
    fc = field_comp(site, "whole", group, 0, 300 * k)
    st = emit("stream", 250, 300 * k)
    wire(fc, 0, st, 0)
    tl = emit("timeline", 0, 300 * k + 150)
    pc = emit("particles", 250, 300 * k + 150)
    wire(fc, 0, pc, 0)
    wire(tl, 0, pc, 1)
    tl.SetTime(10.0, False)
    t0 = time.time()
    solve()
    dt = time.time() - t0
    f = out(fc, 0)[0].Value
    fields[site] = f
    check(f"{site}: Field", f.Valid > 1000 and not msgs(fc),
          f"{f.Valid} cells of {f.Cell:.2f} m, origin E {f.OriginE:.0f} N {f.OriginN:.0f}, "
          f"{len(out(fc, 1))} points out; messages {msgs(fc)}; solve {dt:.1f} s")

    # streamlines: direction of every segment against the field there
    curves = [g.Value for g in out(st, 0)]
    angs = []
    for c in curves:
        ok, pl = c.TryGetPolyline()
        for a, b in zip(list(pl)[:-1], list(pl)[1:]):
            mx, my = (a.X + b.X) / 2, (a.Y + b.Y) / 2
            okf, s = f.Sample(mx, my)
            if okf and s.Speed > 1e-6:
                angs.append(angle(b.X - a.X, b.Y - a.Y, s.Vx, s.Vy))
    angs.sort()
    med = angs[len(angs) // 2] if angs else 99
    p95 = angs[int(len(angs) * 0.95)] if angs else 99
    check(f"{site}: Streamlines aligned", len(curves) > 20 and med < 3 and p95 < 15,
          f"{len(curves)} lines, {len(angs)} segments; angle to the field median {med:.2f} deg, p95 {p95:.2f} deg")

    # particles at t = 10 s: speed against the field under them, none outside
    pts = [g.Value for g in out(pc, 0)]
    spd = [g.Value for g in out(pc, 2)]
    rel, outside, fs_all, ps_all = [], 0, [], []
    for p, v in zip(pts, spd):
        okf, s = f.Sample(p.X, p.Y)
        if not okf:
            outside += 1
            continue
        fs = s.Speed / max(f.Scale, 1e-9)
        fs_all.append(fs)
        ps_all.append(v)
        if fs > 0.05:
            rel.append(abs(v - fs) / fs)
    rel.sort()
    mrel = statistics.median(rel) if rel else 1
    ratio = (sum(ps_all) / max(sum(fs_all), 1e-9)) if fs_all else 0
    check(f"{site}: Particles follow the field speed", len(pts) > 100 and mrel < 0.05 and abs(ratio - 1) < 0.05,
          f"{len(pts)} particles at t=10 s; |particle - field| / field: median {mrel * 100:.2f} %, "
          f"mean speeds {sum(ps_all) / max(len(ps_all), 1):.2f} vs field {sum(fs_all) / max(len(fs_all), 1):.2f} m/s "
          f"(ratio {ratio:.3f})")
    check(f"{site}: Particles inside the field", outside == 0, f"{outside} of {len(pts)} outside")

    # timing: ~3000 particles, ms per step
    ps = ParticleSystem(f)
    ps.Life = 20.0
    ps.Rate = 3000 / 20.0
    ps.StepTo(25.0)
    for _ in range(4):        # tune the birth rate until ~3000 are alive
        if len(ps.Alive) == 0:
            break
        ps.Rate = ps.Rate * 3000.0 / len(ps.Alive)
        ps.StepTo(0.0)
        ps.StepTo(25.0)
    n0 = len(ps.Alive)
    sw = System.Diagnostics.Stopwatch.StartNew()
    for _ in range(300):
        ps.Step()
    ms = sw.Elapsed.TotalMilliseconds / 300
    check(f"{site}: Particle step time", n0 > 2000 and ms < 50,
          f"{n0} particles alive: {ms:.2f} ms per step ({1000 / ms:.0f} steps/s)")

# ---- Blend: weight 0 / 1 give the inputs back exactly ------------------------------------
print("\n== Blend", flush=True)
fa = field_comp("reuters_plaza", "whole", "people", 600, 0)
fb = field_comp("reuters_plaza", "plaza1", "people", 600, 120)
bl = emit("blend", 800, 60)
wire(fa, 0, bl, 0)
wire(fb, 0, bl, 0)
put(bl, 3, 0.0)
put(bl, 4, 0.0)
put(bl, 6, 0.0)
for t_, src_c in ((0.0, fa), (1.0, fb)):
    put(bl, 1, t_)
    solve()
    m = out(bl, 0)[0].Value
    src = out(src_c, 0)[0].Value
    worst = 0.0
    n = 0
    for k in range(src.Count):
        if not src.Has[k]:
            continue
        cw = src.CellWorld(k)
        x, y = cw.Item1, cw.Item2
        ok1, a = m.Sample(x, y)
        ok2, b = src.Sample(x, y)
        if not (ok1 and ok2):
            worst = 1e9
            continue
        n += 1
        worst = max(worst, abs(a.Vx - b.Vx), abs(a.Vy - b.Vy), abs(a.Confidence - b.Confidence),
                    abs(a.Density - b.Density), abs(a.MeanSpeed - b.MeanSpeed), abs(a.Z - b.Z))
    check(f"Blend at T={t_:.0f} equals its input", worst < 1e-5,
          f"{n} cells of {src.Name} compared: largest difference {worst:.2e}")
put(bl, 1, 0.5)
solve()
m = out(bl, 0)[0].Value
check("Blend at T=0.5", m.Valid >= max(out(fa, 0)[0].Value.Valid, out(fb, 0)[0].Value.Valid),
      f"{m.Valid} cells (whole {out(fa, 0)[0].Value.Valid}, plaza1 {out(fb, 0)[0].Value.Valid})")

# ---- Crossings: where two flows overlap ---------------------------------------------------
print("\n== Crossings", flush=True)
for (s1, c1, g1), (s2, c2, g2) in [(("trafalgar_square", "whole", "people"), ("trafalgar_square", "whole", "vehicles")),
                                   (("reuters_plaza", "plaza1", "people"), ("reuters_plaza", "plaza3", "people"))]:
    f1 = field_comp(s1, c1, g1, 1000, 0)
    f2 = field_comp(s2, c2, g2, 1000, 120)
    st1, st2 = emit("stream", 1200, 0), emit("stream", 1200, 120)
    wire(f1, 0, st1, 0)
    wire(f2, 0, st2, 0)
    cr = emit("cross", 1400, 60)
    wire(st1, 0, cr, 0)
    wire(st2, 0, cr, 1)
    solve()
    n = out(cr, 4)[0].Value
    angs = [g.Value for g in out(cr, 1)]
    heat = out(cr, 5)
    check(f"Crossings {c1} {g1} x {c2} {g2}", n > 0 and len(heat) == 1,
          f"{n} crossings of {len(out(st1, 0))} x {len(out(st2, 0))} lines, median angle "
          f"{statistics.median(angs) if angs else 0:.0f} deg, heat mesh {heat[0].Value.Faces.Count if heat else 0} cells")
# a known case: 5 horizontal x 7 vertical lines = 35 crossings
lines_a = [Rhino.Geometry.LineCurve(Rhino.Geometry.Point3d(0, i + 0.5, 0), Rhino.Geometry.Point3d(10, i + 0.5, 0)) for i in range(5)]
lines_b = [Rhino.Geometry.LineCurve(Rhino.Geometry.Point3d(i + 1.25, -1, 0), Rhino.Geometry.Point3d(i + 1.25, 7, 0)) for i in range(7)]
cr = emit("cross", 1400, 300)
for c in lines_a:
    impl(cr.Params.Input[0]).PersistentData.Append(Grasshopper.Kernel.Types.GH_Curve(c))
for c in lines_b:
    impl(cr.Params.Input[1]).PersistentData.Append(Grasshopper.Kernel.Types.GH_Curve(c))
solve()
check("Crossings known grid", out(cr, 4)[0].Value == 35 and all(abs(g.Value - 90) < 1e-6 for g in out(cr, 1)),
      f"{out(cr, 4)[0].Value} crossings (expected 35), angles {sorted(set(round(g.Value) for g in out(cr, 1)))}")

# ---- Timeline + Replay: scrubbing gives the data's positions --------------------------------
print("\n== Timeline + Replay (plaza1)", flush=True)
results = args.results
site_dir = os.path.join(results, "03_reuters_plaza")
origin = tuple(map(float, re.search(r"^ORIGIN = \((-?[\d.]+), (-?[\d.]+)\)",
                                    open(os.path.join(site_dir, "composite", "houdini", "houdini_field.py")).read(),
                                    re.M).groups()))
tracks = {}
with open(os.path.join(site_dir, "clips", "plaza1", "houdini", "points.csv"), newline="") as fh:
    for r in csv.DictReader(fh):
        if r["group"] != "people":
            continue
        tracks.setdefault(int(r["track_id"]), []).append((float(r["time_s"]), float(r["x"]) - origin[0], float(r["y"]) - origin[1]))
for v in tracks.values():
    v.sort()


def truth(tid, t):
    s = tracks[tid]
    for (t0, x0, y0), (t1, x1, y1) in zip(s, s[1:]):
        if t0 <= t <= t1:
            f_ = 0 if t1 == t0 else (t - t0) / (t1 - t0)
            return x0 + f_ * (x1 - x0), y0 + f_ * (y1 - y0)
    if len(s) == 1 and abs(s[0][0] - t) < 1e-9:
        return s[0][1], s[0][2]
    return None


fr = field_comp("reuters_plaza", "plaza1", "people", 1600, 0)
tl = emit("timeline", 1600, 150)
rp = emit("replay", 1800, 60)
wire(fr, 0, rp, 0)
wire(tl, 0, rp, 1)
snap = {}
for t_ in (5.0, 12.5, 5.0, 21.3):
    tl.SetTime(t_, False)
    tl.ExpireSolution(False)
    solve()
    pts = [g.Value for g in out(rp, 0)]
    ids = [g.Value for g in out(rp, 5)]
    errs = []
    for p, i in zip(pts, ids):
        tr_ = truth(i, t_)
        if tr_ is not None:
            errs.append(math.hypot(p.X - tr_[0], p.Y - tr_[1]))
    expected = sum(1 for v in tracks.values() if v[0][0] <= t_ <= v[-1][0])
    worst = max(errs) if errs else 99
    check(f"Replay at T={t_}", len(pts) == expected and worst < 0.1,
          f"{len(pts)} people (data: {expected}); largest distance to the data {worst * 100:.2f} cm "
          f"(Timeline T = {out(tl, 0)[0].Value})")
    key = round(t_, 3)
    pos = sorted((i, round(p.X, 6), round(p.Y, 6)) for p, i in zip(pts, ids))
    if key in snap:
        check(f"Scrub back to T={t_}", snap[key] == pos, "same positions as before the scrub" if snap[key] == pos else "differs")
    snap[key] = pos
# time-sliced field: cells at T match the samples in the window
ft = field_comp("reuters_plaza", "plaza1", "people", 1600, 300)
wire(tl, 0, ft, 4)
put(ft, 5, 4.0)
tl.SetTime(12.0, False)
tl.ExpireSolution(False)
solve()
fs_ = out(ft, 0)[0].Value
fw_ = out(fr, 0)[0].Value
cells = set()
with open(os.path.join(site_dir, "clips", "plaza1", "houdini", "points.csv"), newline="") as fh:
    for r in csv.DictReader(fh):
        if r["group"] == "people" and r["predicted"].lower() not in ("true", "1") and 10.0 <= float(r["time_s"]) <= 14.0:
            i = round((float(r["x"]) - origin[0] - fw_.X0) / fw_.Cell)
            j = round((float(r["y"]) - origin[1] - fw_.Y0) / fw_.Cell)
            if 0 <= i < fw_.Nx and 0 <= j < fw_.Ny:
                cells.add((i, j))
check("Time-sliced field at T=12 s (+-2 s)", fs_.Valid == len(cells) and 0 < fs_.Valid < fw_.Valid,
      f"{fs_.Valid} cells with movement in 10-14 s (from the data: {len(cells)}); whole clip {fw_.Valid}")

# ---- forces, layers, merge, gradient (0.2) -----------------------------------------------------
print("\n== Forces and layers (plaza)", flush=True)
import Rhino.Geometry as RG  # noqa: E402
from Grasshopper.Kernel.Types import GH_Vector, GH_Point, GH_Line, GH_Curve  # noqa: E402
move_guid = next(p_.Guid for p_ in cs.ObjectProxies if str(p_.Desc.Name) == "Move" and str(p_.Desc.Category) == "Transform")


def moved(src, dx, dy, dz, x, y):
    m_ = impl(cs.EmitObject(move_guid))
    m_.CreateAttributes()
    m_.Attributes.Pivot = PointF(x, y)
    doc.AddObject(m_, False)
    impl(m_.Params.Input[0]).AddSource(impl(src.Params.Output[0]))
    impl(m_.Params.Input[1]).PersistentData.Append(GH_Vector(RG.Vector3d(dx, dy, dz)))
    return m_


def particles_with(fields_out, forces=(), t=12.0, **inputs):
    pc_ = emit("particles", 2400, 0)
    for fo in fields_out:
        impl(pc_.Params.Input[0]).AddSource(fo)
    for fr in forces:
        impl(pc_.Params.Input[10]).AddSource(impl(fr.Params.Output[0]))
    put(pc_, 1, t)
    for ix, v in inputs.items():
        put(pc_, int(ix[1:]), v)
    solve()
    return pc_


def alive(pc_):
    return list(zip([g.Value for g in out(pc_, 0)], [g.Value for g in out(pc_, 1)], [g.Value for g in out(pc_, 7)]))


fp = field_comp("reuters_plaza", "whole", "people", 2000, 0)
fp_up = moved(fp, 0, 0, 8.0, 2200, 60)                          # the same plaza 8 m up
solve()
pf_ = out(fp, 0)[0].Value
bb = pf_.Bounds()
cx, cy = (bb.Item1 + bb.Item3) / 2, (bb.Item2 + bb.Item4) / 2

# two layers, 8 m apart: off their plane only with Interact
it = emit("interact", 2200, 200)
put(it, 0, "others"); put(it, 1, 10.0); put(it, 2, 0.3); put(it, 3, 2.0); put(it, 4, 0.3)
offs = {}
for label, frcs in (("no interact", ()), ("interact", (it,))):
    pc_ = particles_with([impl(fp.Params.Output[0]), impl(fp_up.Params.Output[0])], frcs, 10.0, i12=0.15, i15="layer")
    a_ = alive(pc_)
    off = sum(1 for p_, v_, ly in a_ if abs(p_.Z - (0 if ly == 0 else 8)) > 0.5) / max(len(a_), 1)
    offs[label] = (off, len(a_), sum(1 for _, _, ly in a_ if ly == 1))
check("Layers: two stacked plaza layers", offs["no interact"][0] < 0.01 and offs["interact"][0] > 0.2,
      f"off their own layer by > 0.5 m: {offs['no interact'][0] * 100:.0f} % without Interact, "
      f"{offs['interact'][0] * 100:.0f} % with ({offs['interact'][1]} particles, {offs['interact'][2]} in the upper layer)")

# axis force: more swirl around the axis (angular momentum about it)
ax_ = emit("axis", 2200, 300)
impl(ax_.Params.Input[0]).PersistentData.Append(GH_Line(RG.Line(RG.Point3d(cx, cy, 0), RG.Point3d(cx, cy, 10))))
put(ax_, 1, 25.0); put(ax_, 2, 2.0)
lmom = {}
for label, frcs in (("none", ()), ("axis", (ax_,))):
    a_ = alive(particles_with([impl(fp.Params.Output[0])], frcs, 8.0))
    lmom[label] = sum((p_.X - cx) * v_.Y - (p_.Y - cy) * v_.X for p_, v_, _ in a_) / max(len(a_), 1)
check("Axis Force swirls", lmom["axis"] > lmom["none"] + 1.0,
      f"mean angular momentum about the axis {lmom['none']:.2f} -> {lmom['axis']:.2f} m^2/s")

# attract: closer to the point
at_ = emit("attract", 2200, 400)
impl(at_.Params.Input[0]).PersistentData.Append(GH_Point(RG.Point3d(cx, cy, 0)))
put(at_, 1, 1.5); put(at_, 2, 40.0)
dist = {}
for label, frcs in (("none", ()), ("attract", (at_,))):
    a_ = alive(particles_with([impl(fp.Params.Output[0])], frcs, 8.0))
    dist[label] = sum(math.hypot(p_.X - cx, p_.Y - cy) for p_, _, _ in a_) / max(len(a_), 1)
check("Attract pulls in", dist["attract"] < dist["none"] - 1.0, f"mean distance to the point {dist['none']:.1f} -> {dist['attract']:.1f} m")

# obstacle: a 10 x 10 m block in the middle is never entered (checked at several times)
ob_ = emit("obstacle", 2200, 500)
sq_pl = RG.Polyline()
for xx, yy in ((cx - 5, cy - 5), (cx + 5, cy - 5), (cx + 5, cy + 5), (cx - 5, cy + 5), (cx - 5, cy - 5)):
    sq_pl.Add(RG.Point3d(xx, yy, 0))
sq = sq_pl.ToPolylineCurve()
impl(ob_.Params.Input[0]).PersistentData.Append(GH_Curve(sq))
inside_counts = []
pc_ = particles_with([impl(fp.Params.Output[0])], (ob_,), 1.0)
for tt in (3.0, 6.0, 9.0, 12.0):
    put(pc_, 1, tt)
    solve()
    pts_ = [g.Value for g in out(pc_, 0)]
    inside_counts.append(sum(1 for q in pts_ if abs(q.X - cx) < 5 - 1e-6 and abs(q.Y - cy) < 5 - 1e-6))
check("Obstacle never entered", sum(inside_counts) == 0 and len(pts_) > 100,
      f"particles inside the block at 3 / 6 / 9 / 12 s: {inside_counts} ({len(pts_)} alive)")

# field force: plaza1 particles pulled towards plaza3's flow
f1_ = field_comp("reuters_plaza", "plaza1", "people", 2000, 700)
f3_ = field_comp("reuters_plaza", "plaza3", "people", 2000, 800)
ff_ = emit("fieldforce", 2200, 750)
impl(ff_.Params.Input[0]).AddSource(impl(f3_.Params.Output[0]))
put(ff_, 1, 3.0)
solve()
fld3 = out(f3_, 0)[0].Value
align = {}
for label, frcs in (("none", ()), ("field force", (ff_,))):
    a_ = alive(particles_with([impl(f1_.Params.Output[0])], frcs, 8.0, i11=0.5))
    angs_ = []
    for p_, v_, _ in a_:
        ok, s3 = fld3.Sample(p_.X, p_.Y)
        if ok and s3.Speed > 0.2 and math.hypot(v_.X, v_.Y) > 0.05:
            angs_.append(angle(v_.X, v_.Y, s3.Vx, s3.Vy))
    align[label] = (statistics.median(angs_) if angs_ else 180, len(angs_))
check("Field Force turns the flow", align["field force"][0] < align["none"][0] - 5,
      f"angle of plaza1 particles to plaza3's flow (where both are): median {align['none'][0]:.0f} -> {align['field force'][0]:.0f} deg "
      f"({align['field force'][1]} particles)")

# field from curves: streamlines along the drawn curve
cf_ = emit("curvefield", 2000, 900)
arc = RG.Arc(RG.Point3d(0, 0, 0), RG.Point3d(20, 12, 0), RG.Point3d(40, 0, 0)).ToNurbsCurve()
impl(cf_.Params.Input[0]).PersistentData.Append(GH_Curve(arc))
st_ = emit("stream", 2200, 900)
impl(st_.Params.Input[0]).AddSource(impl(cf_.Params.Output[0]))
solve()
fcur = out(cf_, 0)[0].Value
angs_ = []
for c_ in [g.Value for g in out(st_, 0)]:
    ok, pl_ = c_.TryGetPolyline()
    for a_, b_ in zip(list(pl_)[:-1], list(pl_)[1:]):
        okc, tpar = arc.ClosestPoint(RG.Point3d((a_.X + b_.X) / 2, (a_.Y + b_.Y) / 2, 0))
        tan = arc.TangentAt(tpar)
        angs_.append(angle(b_.X - a_.X, b_.Y - a_.Y, tan.X, tan.Y))
angs_.sort()
check("Field from Curves flows along the curve", len(angs_) > 50 and angs_[int(len(angs_) * 0.95)] < 10,
      f"{len(out(st_, 0))} streamlines, angle to the curve's direction: median {angs_[len(angs_) // 2]:.1f}, "
      f"p95 {angs_[int(len(angs_) * 0.95)]:.1f} deg; field {fcur.Valid} cells")

# merge: Trafalgar people + vehicles in one field
mp_ = field_comp("trafalgar_square", "whole", "people", 2000, 1000)
mv_ = field_comp("trafalgar_square", "whole", "vehicles", 2000, 1100)
mg_ = emit("merge", 2200, 1050)
impl(mg_.Params.Input[0]).AddSource(impl(mp_.Params.Output[0]))
impl(mg_.Params.Input[0]).AddSource(impl(mv_.Params.Output[0]))
put(mg_, 1, 0.0); put(mg_, 2, 0.0); put(mg_, 3, False); put(mg_, 4, 0.0)
solve()
fm, fpp, fvv = out(mg_, 0)[0].Value, out(mp_, 0)[0].Value, out(mv_, 0)[0].Value
only_p = 0
for k in range(0, fpp.Count, 7):
    if not fpp.Has[k]:
        continue
    w_ = fpp.CellWorld(k)
    okv, _ = fvv.Sample(w_.Item1, w_.Item2)
    okm, sm_ = fm.Sample(w_.Item1, w_.Item2)
    ok_p, sp_ = fpp.Sample(w_.Item1, w_.Item2)
    if not okv and okm and abs(sm_.Vx - sp_.Vx) < 1e-5 and abs(sm_.Vy - sp_.Vy) < 1e-5:
        only_p += 1
check("Merge Fields", fm.Valid >= max(fpp.Valid, fvv.Valid) and only_p > 100,
      f"{fm.Valid} cells (people {fpp.Valid}, vehicles {fvv.Valid}); where only people moved the merge equals them ({only_p} cells checked)")

# gradient recolours streamlines
gr_ = emit("gradient", 2200, 1200)
put(gr_, 0, "white-pink")
st2_ = emit("stream", 2400, 1200)
impl(st2_.Params.Input[0]).AddSource(impl(fp.Params.Output[0]))
impl(st2_.Params.Input[4]).AddSource(impl(gr_.Params.Output[0]))
solve()
cols_ = [g.Value for g in out(st2_, 2)]
on_line = all(c_.R == 255 and abs((255 - c_.G) / 215.0 - (255 - c_.B) / 55.0) < 0.05 for c_ in cols_)
check("Gradient recolours", len(cols_) > 20 and on_line,
      f"{len(cols_)} streamline colours, all between white and pink: {on_line} (e.g. {cols_[0].R},{cols_[0].G},{cols_[0].B})")

# step time with forces (~3000 particles, interact + axis + obstacle)
from Groundtrack.Core import InteractForce, AxisForce, ObstacleForce, FootprintObstacle  # noqa: E402
import System.Collections.Generic as SCG  # noqa: E402
ps = ParticleSystem(pf_)
ps.Life = 20.0
ps.Rate = 150.0
ps.Hold = 0.5
inter = InteractForce()
inter.Radius = 3.0
inter.Between = InteractForce.Who.All
ps.Forces.Add(inter)
axf = AxisForce()
axf.Ox, axf.Oy, axf.Radius, axf.Orbit = cx, cy, 25.0, 1.0
ps.Forces.Add(axf)
poly = SCG.List[System.ValueTuple[float, float]]()
for xx, yy in ((cx - 5, cy - 5), (cx + 5, cy - 5), (cx + 5, cy + 5), (cx - 5, cy + 5)):
    poly.Add(System.ValueTuple[float, float](xx, yy))
obf = ObstacleForce()
obf.Obstacles.Add(FootprintObstacle(poly))
ps.Forces.Add(obf)
ps.StepTo(25.0)
for _ in range(3):
    if len(ps.Alive):
        ps.Rate = ps.Rate * 3000.0 / len(ps.Alive)
        ps.StepTo(0.0)
        ps.StepTo(25.0)
n0 = len(ps.Alive)
sw = System.Diagnostics.Stopwatch.StartNew()
for _ in range(100):
    ps.Step()
ms = sw.Elapsed.TotalMilliseconds / 100
check("Step time with forces", n0 > 2000 and ms < 100, f"{n0} particles with Interact + Axis + Obstacle: {ms:.1f} ms per step")


# ---- 0.2: smooth merges, flat transforms, Transform / Smooth Field -------------------------------
print("\n== Smooth merges and transforms (Trafalgar)", flush=True)
G["transformfield"] = "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e518"
G["smoothfield"] = "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e519"
ft = field_comp("trafalgar_square", "whole", "people", 2600, 0)
lifted = moved(moved(ft, 0, 0, 26.0, 2700, 40), 50.0, 0, 0, 2800, 40)      # the screenshot's setup


def merged(feather, smooth, fill, slope):
    mg = emit("merge", 2900, 0)
    impl(mg.Params.Input[0]).AddSource(impl(ft.Params.Output[0]))
    impl(mg.Params.Input[0]).AddSource(impl(lifted.Params.Output[0]))
    put(mg, 1, feather); put(mg, 2, smooth); put(mg, 3, fill); put(mg, 4, slope)
    st = emit("stream", 3100, 0)
    impl(st.Params.Input[0]).AddSource(impl(mg.Params.Output[0]))
    solve()
    f_ = out(mg, 0)[0].Value
    steep, worst = 0, 0.0
    curves = [g.Value for g in out(st, 0)]
    nseg = 0
    for c_ in curves:
        ok, pl_ = c_.TryGetPolyline()
        for a_, b_ in zip(list(pl_)[:-1], list(pl_)[1:]):
            run = math.hypot(b_.X - a_.X, b_.Y - a_.Y)
            nseg += 1
            if run > 1e-9:
                slope = abs(b_.Z - a_.Z) / run
                worst = max(worst, slope)
                steep += slope > 2.0
    return f_, len(curves), steep, nseg, worst


hard = merged(0.0, 0.0, False, 0.0)
soft = merged(4.0, 1.5, True, 1.0)
check("Merge: seams smoothed (two Trafalgars, 26 m apart)",
      soft[2] < 0.2 * max(hard[2], 1) and soft[4] < hard[4],
      f"streamline segments steeper than 2:1 - hard merge {hard[2]} of {hard[3]} (steepest {hard[4]:.1f}:1), "
      f"defaults (feather 4 m, smooth 1.5 m, max slope 1) {soft[2]} of {soft[3]} (steepest {soft[4]:.1f}:1)")

# ---- tilting: Grasshopper's own Rotate nodes about horizontal axes tilt / stand fields up
print("\n== Tilting with Grasshopper's Rotate nodes (Trafalgar)", flush=True)
names_ = {}
for p_ in cs.ObjectProxies:
    if str(p_.Desc.Category) == "Transform" and not p_.Obsolete:
        names_[str(p_.Desc.Name)] = p_.Guid
solve()
f_in = out(ft, 0)[0].Value
b_in = f_in.Bounds()
w_in, h_in = b_in.Item3 - b_in.Item1, b_in.Item4 - b_in.Item2
ctr = RG.Point3d((b_in.Item1 + b_in.Item3) / 2, (b_in.Item2 + b_in.Item4) / 2, 0)


def gh_node(name, src, x, y, **inputs):
    n_ = impl(cs.EmitObject(names_[name]))
    n_.CreateAttributes()
    n_.Attributes.Pivot = PointF(x, y)
    doc.AddObject(n_, False)
    impl(n_.Params.Input[0]).AddSource(impl(src.Params.Output[0]))
    for ix, goo in inputs.items():
        prm = impl(n_.Params.Input[int(ix[1:])])
        prm.PersistentData.Clear()
        prm.PersistentData.Append(goo)
    return n_


def stream_check(src_param, f_t):
    """Streamlines of a (tilted) field: on its ground and along its flow."""
    st_ = emit("stream", 3000, 600)
    impl(st_.Params.Input[0]).AddSource(src_param)
    solve()
    worst_d, angs, n_l = 0.0, [], 0
    for g_ in out(st_, 0):
        ok_, pl_ = g_.Value.TryGetPolyline()
        if not ok_:
            continue
        n_l += 1
        for k_ in range(1, pl_.Count):
            q0, q1 = pl_[k_ - 1], pl_[k_]
            mx_, my_, mz_ = (q0.X + q1.X) / 2, (q0.Y + q1.Y) / 2, (q0.Z + q1.Z) / 2
            ok2, sm = f_t.SampleWorld(mx_, my_, mz_)
            if not ok2 or sm.Speed < 1e-6:
                continue
            nx_, ny_, nz_ = f_t.Place.Normal.Item1, f_t.Place.Normal.Item2, f_t.Place.Normal.Item3
            ok3, sq = f_t.SampleWorld(q1.X, q1.Y, q1.Z)
            if ok3:
                worst_d = max(worst_d, abs((q1.X - sq.Gx) * nx_ + (q1.Y - sq.Gy) * ny_ + (q1.Z - sq.Z) * nz_))
            dx_, dy_, dz_ = q1.X - q0.X, q1.Y - q0.Y, q1.Z - q0.Z
            dn_ = dx_ * nx_ + dy_ * ny_ + dz_ * nz_          # along the field's plane (slopes of its ground aside)
            dx_, dy_, dz_ = dx_ - dn_ * nx_, dy_ - dn_ * ny_, dz_ - dn_ * nz_
            dl = math.sqrt(dx_ * dx_ + dy_ * dy_ + dz_ * dz_)
            if dl < 1e-9:
                continue
            c_ = (dx_ * sm.Vx + dy_ * sm.Vy + dz_ * sm.Vz) / (dl * sm.Speed)
            angs.append(math.degrees(math.acos(max(-1.0, min(1.0, c_)))))
    doc.RemoveObject(st_, False)
    angs.sort()
    return n_l, worst_d, (angs[int(0.95 * (len(angs) - 1))] if angs else 99)


def particle_check(src_param, f_t):
    pc_ = particles_with([src_param], t=12.0)
    outside, devs, worst_d = 0, [], 0.0
    nx_, ny_, nz_ = f_t.Place.Normal.Item1, f_t.Place.Normal.Item2, f_t.Place.Normal.Item3
    for p_, v_, _ in alive(pc_):
        ok_, sm = f_t.SampleWorld(p_.X, p_.Y, p_.Z)
        if not ok_:
            outside += 1
            continue
        worst_d = max(worst_d, abs((p_.X - sm.Gx) * nx_ + (p_.Y - sm.Gy) * ny_ + (p_.Z - sm.Z) * nz_))
        if sm.Speed > 0.05:
            devs.append(abs(v_.Length / sm.Speed - 1))
    n_ = len(alive(pc_))
    doc.RemoveObject(pc_, False)
    devs.sort()
    return n_, outside, worst_d, (devs[len(devs) // 2] if devs else 99)


yz = RG.Plane.WorldYZ
routes = [
    ("Rotate, YZ plane, angle 360 (as in the screenshot: radians)", "Rotate",
     dict(i1=GH_Number(360.0), i2=Grasshopper.Kernel.Types.GH_Plane(yz))),
    ("Rotate 3D, 60 deg about X through the centre", "Rotate 3D",
     dict(i1=GH_Number(math.radians(60)), i2=GH_Point(ctr), i3=GH_Vector(RG.Vector3d(1, 0, 0)))),
    ("Rotate Axis, 90 deg about a north-south line (stood up)", "Rotate Axis",
     dict(i1=GH_Number(math.radians(90)), i2=GH_Line(RG.Line(ctr, ctr + RG.Vector3d(0, 50, 0))))),
    ("Orient onto a vertical plane", "Orient",
     dict(i1=Grasshopper.Kernel.Types.GH_Plane(RG.Plane.WorldXY),
          i2=Grasshopper.Kernel.Types.GH_Plane(RG.Plane(RG.Point3d(400, 0, 0), RG.Vector3d(1, 0, 0), RG.Vector3d(0, 0, 1))))),
    ("Rotate in plan (XY plane, 0.6 rad): stays level", "Rotate",
     dict(i1=GH_Number(0.6), i2=Grasshopper.Kernel.Types.GH_Plane(RG.Plane.WorldXY))),
]
for label, node, ins in routes:
    rn = gh_node(node, ft, 3000, 300, **ins)
    solve()
    errs = msgs(rn)
    f_t = impl(list(impl(rn.Params.Output[0]).VolatileData.AllData(True))[0]).Value
    xf_ = impl(list(impl(rn.Params.Output[1]).VolatileData.AllData(True))[0]).Value
    worst_p, worst_v, n_c = 0.0, 0.0, 0
    for k in range(0, f_in.Count, 37):
        if not f_in.Has[k]:
            continue
        n_c += 1
        w_ = f_in.CellWorld(k)
        e_ = RG.Point3d(w_.Item1, w_.Item2, w_.Item3)
        e_.Transform(xf_)
        g_ = f_t.CellWorld(k)
        worst_p = max(worst_p, e_.DistanceTo(RG.Point3d(g_.Item1, g_.Item2, g_.Item3)))
        v_ = f_in.CellVelocity(k)
        ev = RG.Vector3d(v_.Item1, v_.Item2, v_.Item3)
        ev.Transform(xf_)
        gv = f_t.CellVelocity(k)
        worst_v = max(worst_v, (ev - RG.Vector3d(gv.Item1, gv.Item2, gv.Item3)).Length)
    bx = f_t.WorldBox()
    size = f"{bx.Item4 - bx.Item1:.1f} x {bx.Item5 - bx.Item2:.1f} x {bx.Item6 - bx.Item3:.1f} m"
    n_l, d_l, a95 = stream_check(impl(rn.Params.Output[0]), f_t)
    n_p, out_p, d_p, dev = particle_check(impl(rn.Params.Output[0]), f_t)
    tilted = f_t.Place.Tilted
    level_ok = tilted if "level" not in label else not tilted
    check(f"Tilt: {label}", not errs and level_ok and worst_p < 1e-6 and worst_v < 1e-6 and n_l > 50 and d_l < 1e-6
          and a95 < 3 and n_p > 500 and out_p == 0 and d_p < 1e-6 and dev < 0.05,
          f"tilted {tilted}; box {size} (flat {w_in:.1f} x {h_in:.1f}); {n_c} cells where the transform puts them "
          f"(largest error {worst_p:.1e} m, velocity {worst_v:.1e} m/s); {n_l} streamlines on its ground "
          f"(off {d_l:.1e} m, angle to the flow p95 {a95:.2f} deg); {n_p} particles: {out_p} outside, "
          f"off the ground {d_p:.1e} m, speed vs the field median {dev * 100:.2f} %")
    doc.RemoveObject(rn, False)

# replay follows a tilted field
r3 = gh_node("Rotate 3D", fr, 3000, 500, i1=GH_Number(math.radians(50)), i3=GH_Vector(RG.Vector3d(1, 0, 0)))
rp_t = emit("replay", 3200, 500)
impl(rp_t.Params.Input[0]).AddSource(impl(r3.Params.Output[0]))
wire(tl, 0, rp_t, 1)
tl.SetTime(12.5, False)
tl.ExpireSolution(False)
solve()
xf_ = impl(list(impl(r3.Params.Output[1]).VolatileData.AllData(True))[0]).Value
flat_pts = {i: p for p, i in zip([g.Value for g in out(rp, 0)], [g.Value for g in out(rp, 5)])}
worst_r, n_r = 0.0, 0
for p_, i in zip([g.Value for g in out(rp_t, 0)], [g.Value for g in out(rp_t, 5)]):
    if i in flat_pts:
        e_ = RG.Point3d(flat_pts[i])
        e_.Transform(xf_)
        worst_r = max(worst_r, e_.DistanceTo(p_))
        n_r += 1
check("Tilt: Replay follows a field rotated 50 deg about X", n_r > 5 and worst_r < 1e-6,
      f"{n_r} people at T=12.5: largest distance to the rotated real positions {worst_r:.1e} m")

# a tilted and a flat field merge (the tilted one sampled across its plane)
st_up = gh_node("Rotate 3D", ft, 3000, 700, i1=GH_Number(math.radians(20)), i2=GH_Point(ctr), i3=GH_Vector(RG.Vector3d(1, 0, 0)))
mg_t = emit("merge", 3200, 700)
impl(mg_t.Params.Input[0]).AddSource(impl(st_up.Params.Output[0]))
impl(mg_t.Params.Input[0]).AddSource(impl(ft.Params.Output[0]))
solve()
f_m = out(mg_t, 0)[0].Value
check("Tilt: Merge a tilted and a flat field", not msgs(mg_t) and f_m.Valid > 0 and f_m.Place.Tilted,
      f"{f_m.Valid} cells, in the tilted field's plane")

# Transform Field: 30 degrees about the field's centre, velocities turn with it
tf = emit("transformfield", 2600, 300)
impl(tf.Params.Input[0]).AddSource(impl(ft.Params.Output[0]))
put(tf, 2, 30.0)
solve()
f_t = out(tf, 0)[0].Value
bc = ((b_in.Item1 + b_in.Item3) / 2, (b_in.Item2 + b_in.Item4) / 2)
c30, s30 = math.cos(math.radians(30)), math.sin(math.radians(30))
worst_v, n_ = 0.0, 0
for k in range(0, f_in.Count, 53):
    if not f_in.Has[k]:
        continue
    w_ = f_in.CellWorld(k)
    v_ = f_in.CellVelocity(k)
    dx, dy = w_.Item1 - bc[0], w_.Item2 - bc[1]
    x2, y2 = bc[0] + c30 * dx - s30 * dy, bc[1] + s30 * dx + c30 * dy
    ok, s_ = f_t.Sample(x2, y2)
    if ok:
        n_ += 1
        worst_v = max(worst_v, math.hypot(s_.Vx - (c30 * v_.Item1 - s30 * v_.Item2), s_.Vy - (s30 * v_.Item1 + c30 * v_.Item2)))
xf_ = out(tf, 1)[0].Value
cpt = RG.Point3d(bc[0], bc[1], 0)
cpt.Transform(xf_)
check("Transform Field turns 30 deg about its centre", n_ > 200 and worst_v < 1e-4 and
      math.hypot(cpt.X - bc[0], cpt.Y - bc[1]) < 1e-6,
      f"{n_} cells: largest velocity error {worst_v:.1e} m/s; the centre maps to itself ({math.hypot(cpt.X - bc[0], cpt.Y - bc[1]):.1e} m)")

# Smooth Field node
sf = emit("smoothfield", 2600, 400)
impl(sf.Params.Input[0]).AddSource(impl(ft.Params.Output[0]))
put(sf, 1, 3.0)
solve()
f_s = out(sf, 0)[0].Value
put(sf, 2, True)
solve()
f_sf = out(sf, 0)[0].Value
check("Smooth Field", f_s.Valid == f_in.Valid and f_sf.Valid > f_in.Valid,
      f"{f_in.Valid} cells -> smoothed {f_s.Valid}, with Fill {f_sf.Valid}")


# ---- levels joined by ramps, and a folded surface ----------------------------------------------
print("\n== Ramps between levels, folded surface (plaza)", flush=True)
G["curvefield"] = "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e509"
RAMP_GAP, RAMP_RISE = 30.0, 5.0


def ramp_curves(lower, upper):
    """Two straight ramps north across the gap from a lower field to an upper one north of it, in
    2 m columns both fields reach: up where most people leave the lower field northwards, down (at
    least 10 m beside it) where most come into it from the north. Each runs from 4 m inside the
    lower field's data to 4 m inside the upper's, flat at both ends."""
    def columns(f):
        cols = {}
        for k in range(f.Count):
            if f.Has[k]:
                w = f.CellWorld(k)
                c = cols.setdefault(round(w.Item1 / 2.0) * 2.0, [1e9, -1e9, 0.0, 0.0])
                c[0] = min(c[0], w.Item2)
                c[1] = max(c[1], w.Item2)
        for k in range(f.Count):
            if f.Has[k]:
                w = f.CellWorld(k)
                c = cols[round(w.Item1 / 2.0) * 2.0]
                if w.Item2 >= c[1] - 6:
                    vy = f.CellVelocity(k).Item2 * f.Density[k]
                    if vy > 0:
                        c[2] += vy
                    else:
                        c[3] -= vy
        return cols

    lo, hi = columns(lower), columns(upper)
    common = [x for x in lo if x in hi and lo[x][1] - lo[x][0] > 10 and hi[x][1] - hi[x][0] > 10]
    x_up = max(common, key=lambda x: lo[x][2])
    x_dn = max((x for x in common if abs(x - x_up) >= 10), key=lambda x: lo[x][3])

    def ground(f, x, y, default):
        ok_, s_ = f.Sample(x, y)
        return s_.Z if ok_ else default

    def curve(x, upwards):
        y0, y1 = lo[x][1], hi[x][0]          # the lower field's north end, the upper's south end here
        zl, zu = ground(lower, x, y0 - 1, 0.0), ground(upper, x, y1 + 1, RAMP_RISE)
        poly = RG.Polyline()
        for i in range(81):
            y = (y0 - 4) + (y1 - y0 + 8) * i / 80.0
            t = min(1.0, max(0.0, (y - y0) / (y1 - y0)))
            poly.Add(RG.Point3d(x, y, zl + (zu - zl) * t * t * (3 - 2 * t)))
        if not upwards:
            poly.Reverse()
        return RG.PolylineCurve(poly)

    return [curve(x_up, True), curve(x_dn, False)], x_up, x_dn


def flow_axis(f):
    """The direction along a field's main flow - from where it starts to where it ends, so that
    copies laid end to end line up (a road that bends slightly arrives where the next copy's road
    begins) - and the field's extent along it (a0, a1)."""
    c2 = s2 = 0.0
    for k in range(f.Count):
        if f.Has[k] and f.Speed[k] > 0.5:
            a = math.atan2(f.Vy[k], f.Vx[k])
            c2 += f.Density[k] * math.cos(2 * a)
            s2 += f.Density[k] * math.sin(2 * a)
    ang = 0.5 * math.atan2(s2, c2)
    u = (math.cos(ang), math.sin(ang))
    pts = [(f.CellWorld(k).Item1, f.CellWorld(k).Item2) for k in range(f.Count) if f.Has[k]]
    al = [x * u[0] + y * u[1] for x, y in pts]
    lo, hi = min(al), max(al)
    s_ = [q for q, a in zip(pts, al) if a < lo + 25]
    e_ = [q for q, a in zip(pts, al) if a > hi - 25]
    dx = sum(q[0] for q in e_) / len(e_) - sum(q[0] for q in s_) / len(s_)
    dy = sum(q[1] for q in e_) / len(e_) - sum(q[1] for q in s_) / len(s_)
    d = math.hypot(dx, dy)
    u = (dx / d, dy / d)
    al = [x * u[0] + y * u[1] for x, y in pts]
    return u, min(al) - f.WorldCell / 2, max(al) + f.WorldCell / 2


def fold_steps(xoff, f, angle1=30.0, angle2=60.0):
    """Grasshopper Move / Rotate 3D steps for three copies of a field hinged edge to edge along its
    main flow: a floor, a slope (angle1) and, after it, a wall (angle1 + angle2)."""
    u, a0, a1 = flow_axis(f)
    L = a1 - a0
    t1 = math.radians(angle1)
    axis = GH_Vector(RG.Vector3d(u[1], -u[0], 0))          # u x Z: turning about it lifts what lies ahead
    h1 = RG.Point3d(xoff + u[0] * a1, u[1] * a1, 0)
    h2 = RG.Point3d(h1.X + u[0] * L * math.cos(t1), h1.Y + u[1] * L * math.cos(t1), L * math.sin(t1))
    rot1 = ("Rotate 3D", {1: GH_Number(t1), 2: GH_Point(h1), 3: axis})
    rot2 = ("Rotate 3D", {1: GH_Number(math.radians(angle2)), 2: GH_Point(h2), 3: axis})
    mv = [("Move", {1: GH_Vector(RG.Vector3d(xoff + n * L * u[0], n * L * u[1], 0))}) for n in range(3)]
    return [[mv[0]], [mv[1], rot1], [mv[2], rot1, rot2]]


def gh_chain(adder, src, steps, x, y, collect=None):
    """Grasshopper's own Transform nodes in a row (Move, Rotate 3D ...)."""
    node = src
    for i, (name, ins) in enumerate(steps):
        n_ = adder(impl(cs.EmitObject(names_[name])), x + 150 * i, y)
        n_.Hidden = True
        if collect is not None:
            collect.append(n_)
        impl(n_.Params.Input[0]).AddSource(impl(node.Params.Output[0]))
        for ix, goo in ins.items():
            prm = impl(n_.Params.Input[ix])
            prm.PersistentData.Clear()
            prm.PersistentData.Append(goo)
        node = n_
    return node


def add_main(obj, x, y):
    obj.CreateAttributes()
    obj.Attributes.Pivot = PointF(x, y)
    doc.AddObject(obj, False)
    return obj


# levels joined by ramps: plaza at 0 m, plaza 3 north of it, 5 m up; two ramps; merged
lo_f = field_comp("reuters_plaza", "whole", "people", 3400, 0)
hi_f = field_comp("reuters_plaza", "plaza3", "people", 3400, 100)
solve()
lb_ = out(lo_f, 0)[0].Value.Bounds()
hb_ = out(hi_f, 0)[0].Value.Bounds()
hi_t = emit("transformfield", 3600, 100)
wire(hi_f, 0, hi_t, 0)
impl(hi_t.Params.Input[1]).PersistentData.Clear()
impl(hi_t.Params.Input[1]).PersistentData.Append(GH_Vector(RG.Vector3d(0, lb_.Item4 + RAMP_GAP - hb_.Item2, RAMP_RISE)))
solve()
lower_, upper_ = out(lo_f, 0)[0].Value, out(hi_t, 0)[0].Value
ramps, up_x, down_x = ramp_curves(lower_, upper_)
cf = emit("curvefield", 3600, 200)
impl(cf.Params.Input[0]).PersistentData.Clear()
for c_ in ramps:
    impl(cf.Params.Input[0]).PersistentData.Append(GH_Curve(c_))
mg_r = emit("merge", 3800, 100)
for src_ in (lo_f, hi_t, cf):
    impl(mg_r.Params.Input[0]).AddSource(impl(src_.Params.Output[0]))
st_r = emit("stream", 4000, 100)
wire(mg_r, 0, st_r, 0)
solve()
f_r = out(mg_r, 0)[0].Value
steep, segs_, climbing = 0, 0, 0
for g_ in out(st_r, 0):
    ok_, pl_ = g_.Value.TryGetPolyline()
    if not ok_:
        continue
    zs_ = [q.Z for q in pl_]
    if min(zs_) < 0.5 and max(zs_) > RAMP_RISE - 0.5:
        climbing += 1
    for k_ in range(1, pl_.Count):
        run_ = math.hypot(pl_[k_].X - pl_[k_ - 1].X, pl_[k_].Y - pl_[k_ - 1].Y)
        segs_ += 1
        if abs(pl_[k_].Z - pl_[k_ - 1].Z) > 2 * max(run_, 1e-9):
            steep += 1
ps = ParticleSystem(f_r)
ps.Rate, ps.Life, ps.Seed = 300.0, 120.0, 1
lowseen, highseen, between, downs, outside_r = set(), set(), set(), set(), 0
for t_ in range(1, 91):
    ps.StepTo(float(t_))
    for p_ in ps.Alive:
        if p_.Z < 0.5:
            lowseen.add(p_.Id)
            if p_.Id in highseen:
                downs.add(p_.Id)
        elif p_.Z > RAMP_RISE - 0.5:
            highseen.add(p_.Id)
            if p_.Id in lowseen:
                between.add(p_.Id)
for p_ in ps.Alive:
    if not f_r.Sample(p_.X, p_.Y)[0]:
        outside_r += 1
check("Ramps join two levels into one continuous flow",
      len(ramps) == 2 and steep == 0 and climbing > 0 and len(between) > 20 and len(downs) > 5 and outside_r == 0,
      f"ramps at x {up_x:.0f} m (up) and {down_x:.0f} m (down); merged {f_r.Valid} cells; {segs_} streamline segments, "
      f"{steep} steeper than 2:1; {climbing} streamlines run between the levels; in 90 s {len(between)} particles walked "
      f"from the plaza up onto the upper level and {len(downs)} down; {outside_r} outside")

# a folded surface: floor, 30 deg slope, wall - Grasshopper's Move + Rotate 3D, particles join
FOLD_JOIN = 8.0
fb_raw = field_comp("blackwall_motorway", "motorway5", "vehicles", 3300, 400)
fb_ = emit("smoothfield", 3400, 400)                 # close holes in the lanes first
wire(fb_raw, 0, fb_, 0)
put(fb_, 1, 4.0)
put(fb_, 2, True)
solve()
fold_base = out(fb_, 0)[0].Value
fold_nodes = [gh_chain(add_main, fb_, st_, 3600, 400 + 60 * i) for i, st_ in enumerate(fold_steps(0.0, fold_base))]
solve()
fold_fields = [impl(list(impl(n_.Params.Output[0]).VolatileData.AllData(True))[0]).Value for n_ in fold_nodes]
hinge_gap = 0.0
for a_, b_ in ((0, 1), (1, 2)):
    # the copies meet: the north edge of one is the south edge of the next
    fa, fb2 = fold_fields[a_], fold_fields[b_]
    u_, a0_, a1_ = flow_axis(fold_base)
    pa = fa.Place.Apply(u_[0] * a1_, u_[1] * a1_, 0.0)
    pb = fb2.Place.Apply(u_[0] * a0_, u_[1] * a0_, 0.0)
    hinge_gap = max(hinge_gap, math.sqrt((pa.Item1 - pb.Item1) ** 2 + (pa.Item2 - pb.Item2) ** 2 + (pa.Item3 - pb.Item3) ** 2))
counts = {}
for join_ in (0.0, FOLD_JOIN):
    ps = ParticleSystem()
    for f_ in fold_fields:
        ps.Fields.Add(f_)
    ps.Rate, ps.Life, ps.Seed, ps.Join = 300.0, 120.0, 1, join_
    first, reached = {}, {}
    for t_ in range(1, 91):
        ps.StepTo(float(t_))
        for p_ in ps.Alive:
            first.setdefault(p_.Id, p_.Layer)
            reached[p_.Id] = max(reached.get(p_.Id, 0), p_.Layer)
    off_ground = 0.0
    for p_ in ps.Alive:
        if p_.Bridge > 0:                    # crossing a gap between copies
            continue
        f_ = fold_fields[p_.Layer]
        ok_, s_ = f_.SampleWorld(p_.X, p_.Y, p_.Z)
        n_ = f_.Place.Normal
        off_ground = max(off_ground, abs((p_.X - s_.Gx) * n_.Item1 + (p_.Y - s_.Gy) * n_.Item2 + (p_.Z - s_.Z) * n_.Item3) if ok_ else 99)
    counts[join_] = (sum(1 for i_ in first if first[i_] == 0 and reached[i_] >= 1),
                     sum(1 for i_ in first if first[i_] == 0 and reached[i_] == 2),
                     sum(1 for p_ in ps.Alive if p_.Joined > 0), off_ground)
pc_f = particles_with([impl(n_.Params.Output[0]) for n_ in fold_nodes], t=40.0, i15="layer", i17=FOLD_JOIN)
n_gh = len(out(pc_f, 0))
put(pc_f, 17, 0.0)
solve()
n_gh0 = len(out(pc_f, 0))
J_ = FOLD_JOIN
check("Folded surface: particles carry on from the floor up the slope and the wall (Join)",
      hinge_gap < 1e-6 and counts[J_][0] > 500 and counts[J_][1] > 100 and counts[J_][3] < 1e-6
      and counts[0.0][0] == 0 and counts[0.0][2] == 0 and n_gh > n_gh0,
      f"motorway 5 (Smooth Field 4 m, Fill) three times (floor, 30 deg slope, wall); copies meet at the hinges (gap {hinge_gap:.1e} m); in 90 s "
      f"with Join {J_:.0f} m: {counts[J_][0]} vehicles born on the floor went up onto the slope, {counts[J_][1]} on up "
      f"the wall, {counts[J_][2]} alive after a hand-over, off their ground {counts[J_][3]:.1e} m; Join 0: "
      f"{counts[0.0][0]} / {counts[0.0][2]}; Particles node at 40 s: {n_gh} alive with Join, {n_gh0} without")


# ---- 3D shaping (0.4): morphs, Warp Field, Landscape, Loft, Bridge, Apply Forces, Intersect, Surface ----
print("\n== 3D shaping (plaza)", flush=True)
for key, n in (("warp", 520), ("landscape", 521), ("loft", 522), ("bridge", 523), ("applyforces", 524),
               ("intersect", 525), ("surface", 526)):
    G[key] = f"3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e{n}"
from Groundtrack.Core import Shape  # noqa: E402

morph_names = {}
for p_ in cs.ObjectProxies:
    if str(p_.Desc.Category) == "Transform" and str(p_.Desc.SubCategory) == "Morph" and not p_.Obsolete:
        morph_names[str(p_.Desc.Name)] = p_.Guid
names_.update(morph_names)
pz = field_comp("reuters_plaza", "whole", "people", 4400, 0)
solve()
pz_f = out(pz, 0)[0].Value
pzb = pz_f.WorldBox()
pc_x, pc_y = (pzb.Item1 + pzb.Item4) / 2, (pzb.Item2 + pzb.Item5) / 2


def sheet_check(field_out, f_t, t=10.0):
    """Particles and streamlines on a (bent) field: on its ground, inside, at the field's speed."""
    st_ = emit("stream", 4600, 900)
    impl(st_.Params.Input[0]).AddSource(field_out)
    solve()
    off_l, n_l = 0.0, 0
    for g_ in out(st_, 0):
        ok_, pl_ = g_.Value.TryGetPolyline()
        if not ok_:
            continue
        n_l += 1
        for q in pl_:
            okq, sq = f_t.SampleWorld(q.X, q.Y, q.Z)
            off_l = max(off_l, math.sqrt((q.X - sq.Gx) ** 2 + (q.Y - sq.Gy) ** 2 + (q.Z - sq.Z) ** 2) if okq else 99)
    doc.RemoveObject(st_, False)
    pc_ = particles_with([field_out], t=t)
    a_ = alive(pc_)
    outside, off_p, devs = 0, 0.0, []
    for p_, v_, _ in a_:
        ok_, sm = f_t.SampleWorld(p_.X, p_.Y, p_.Z)
        if not ok_:
            outside += 1
            continue
        off_p = max(off_p, math.sqrt((p_.X - sm.Gx) ** 2 + (p_.Y - sm.Gy) ** 2 + (p_.Z - sm.Z) ** 2))
        if sm.Speed > 0.05:
            devs.append(abs(v_.Length / sm.Speed - 1))
    doc.RemoveObject(pc_, False)
    devs.sort()
    dev = devs[len(devs) // 2] if devs else 99
    zs = [p_.Z for p_, _, _ in a_]
    return (n_l, off_l, len(a_), outside, off_p, dev, (max(zs) - min(zs)) if zs else 0)


def sheet_ok(r):
    n_l, off_l, n_p, outside, off_p, dev, _ = r
    return n_l > 10 and off_l < 0.05 and n_p > 300 and outside == 0 and off_p < 0.05 and dev < 0.05


def sheet_text(r):
    n_l, off_l, n_p, outside, off_p, dev, zr = r
    return (f"{n_l} streamlines (off the sheet {off_l:.3f} m); {n_p} particles: {outside} outside, off the sheet {off_p:.3f} m, "
            f"speed vs the field {dev * 100:.2f} %, spread over {zr:.1f} m in height")


# Grasshopper's own morph nodes on a field
ax_line = GH_Line(RG.Line(RG.Point3d(pc_x, pzb.Item2, 0), RG.Point3d(pc_x, pzb.Item5, 0)))
for label, node, ins in (
        ("Twist 180 deg about the plaza's long axis", "Twist", dict(i1=ax_line, i2=GH_Number(math.pi))),
        ("Maelstrom (vortex)", "Maelstrom", dict(i1=Grasshopper.Kernel.Types.GH_Plane(RG.Plane(RG.Point3d(pc_x, pc_y, 0), RG.Vector3d.ZAxis)),
                                                  i2=GH_Number(5.0), i3=GH_Number(40.0), i4=GH_Number(math.radians(120)))),
        ("Bend Deform along an arc", "Bend Deform", dict(i1=Grasshopper.Kernel.Types.GH_Arc(RG.Arc(
            RG.Point3d(pc_x, pzb.Item2, 0), RG.Point3d(pc_x, pc_y, 12), RG.Point3d(pc_x, pzb.Item5, 0))))),
):
    mn = gh_node(node, pz, 4600, 0, **ins)
    solve()
    errs = msgs(mn)
    outs_ = list(impl(mn.Params.Output[0]).VolatileData.AllData(True))
    f_m = impl(outs_[0]).Value if outs_ else None
    r = sheet_check(impl(mn.Params.Output[0]), f_m) if f_m is not None else (0, 99, 0, 0, 99, 99, 0)
    check(f"Morph: Grasshopper {label}", not errs and f_m is not None and f_m.Warped and sheet_ok(r), sheet_text(r))
    doc.RemoveObject(mn, False)

# Warp Field, every mode
for mode in ("twist", "bend", "swirl", "wave", "taper", "bulge"):
    wf = emit("warp", 4600, 100)
    wire(pz, 0, wf, 0)
    put(wf, 1, mode)
    solve()
    f_w = out(wf, 0)[0].Value
    r = sheet_check(impl(wf.Params.Output[0]), f_w)
    check(f"Warp Field {mode}", not msgs(wf) and f_w.Warped and sheet_ok(r), f"{wf.Message}: " + sheet_text(r))
    doc.RemoveObject(wf, False)

# Landscape: relief by density, softened, and draped over a modelled surface
ld = emit("landscape", 4600, 200)
wire(pz, 0, ld, 0)
put(ld, 1, 6.0)
put(ld, 3, 8.0)
solve()
f_l = out(ld, 0)[0].Value
zs_l = [f_l.CellWorld(k).Item3 for k in range(f_l.Count) if f_l.Has[k]]
srf = RG.NurbsSurface.CreateFromCorners(RG.Point3d(pzb.Item1 - 5, pzb.Item2 - 5, 2), RG.Point3d(pzb.Item4 + 5, pzb.Item2 - 5, 2),
                                        RG.Point3d(pzb.Item4 + 5, pzb.Item5 + 5, 14), RG.Point3d(pzb.Item1 - 5, pzb.Item5 + 5, 14))
ld2 = emit("landscape", 4600, 300)
wire(pz, 0, ld2, 0)
impl(ld2.Params.Input[4]).PersistentData.Append(Grasshopper.Kernel.Types.GH_Surface(srf.ToBrep()))
solve()
f_d = out(ld2, 0)[0].Value
worst_d = 0.0
for k in range(0, f_d.Count, 11):
    if not f_d.Has[k]:
        continue
    w_ = f_d.CellWorld(k)
    t_ = (w_.Item2 - (pzb.Item2 - 5)) / ((pzb.Item5 + 5) - (pzb.Item2 - 5))
    worst_d = max(worst_d, abs(w_.Item3 - (2 + 12 * t_)))
r_d = sheet_check(impl(ld2.Params.Output[0]), f_d)
check("Landscape: relief 8 m by density, softened 6 m; draped over a sloping surface",
      max(zs_l) - min(zs_l) > 6 and worst_d < 0.05 and r_d[3] == 0 and r_d[5] < 0.05,
      f"relief: ground from {min(zs_l):.1f} to {max(zs_l):.1f} m; draped: largest height error {worst_d:.3f} m; "
      f"particles on it: {r_d[2]}, {r_d[3]} outside, speed vs the field {r_d[5] * 100:.2f} %")

# Loft: three stacked plaza layers 8 m apart joined at their edges
lofted = []
for i_, (clip_, dz_) in enumerate((("whole", 16.0), ("plaza3", 8.0), ("plaza1", 0.0))):
    fl_ = field_comp("reuters_plaza", clip_, "people", 4400, 400 + 60 * i_)
    lofted.append(moved(fl_, 0, 0, dz_, 4500, 400 + 60 * i_))
lf = emit("loft", 4700, 450)
for m_ in lofted:
    impl(lf.Params.Input[0]).AddSource(impl(m_.Params.Output[0]))
solve()
folds = [g_.Value for g_ in out(lf, 0)]
all_l = [g_.Value for g_ in out(lf, 1)]
ends_ok = True
for fo_, (ztop, zbot) in zip(folds, ((16.0, 8.0), (8.0, 0.0))):
    z_first = statistics.median(fo_.CellWorld(i_).Item3 for i_ in range(fo_.Nx))
    z_last = statistics.median(fo_.CellWorld((fo_.Ny - 1) * fo_.Nx + i_).Item3 for i_ in range(fo_.Nx))
    ends_ok = ends_ok and abs(z_first - ztop) < 0.5 and abs(z_last - zbot) < 0.5
ps = ParticleSystem()
for f_ in all_l:
    ps.Fields.Add(f_)
ps.Rate, ps.Life, ps.Seed, ps.Join = 300.0, 120.0, 1, 2.0
first, reached_l, top_down = {}, {}, 0
for t_ in range(1, 91):
    ps.StepTo(float(t_))
    for p_ in ps.Alive:
        first.setdefault(p_.Id, p_.Layer)
        reached_l[p_.Id] = reached_l.get(p_.Id, set()) | {p_.Layer}
over_fold = sum(1 for i_ in first if first[i_] in (0, 2, 4) and len(reached_l[i_] & {1, 3}) > 0)
layer_to_layer = sum(1 for i_ in first if first[i_] in (0, 2, 4) and len(reached_l[i_] & {0, 2, 4}) > 1)
r_f = sheet_check(impl(lf.Params.Output[1]), folds[0]) if folds else (0, 99, 0, 0, 99, 99, 0)
check("Loft Fields: three plaza layers 8 m apart joined at their edges",
      len(folds) == 2 and len(all_l) == 5 and ends_ok and over_fold > 50 and layer_to_layer > 10,
      f"{len(folds)} folds ({', '.join(f'{fo_.Valid} cells' for fo_ in folds)}), starting and ending on the layers' heights: "
      f"{ends_ok}; in 90 s {over_fold} particles went from a layer onto a fold, {layer_to_layer} on to another layer")

# Bridge: the plaza and plaza 3 (40 m north, 6 m up): automatic ribbons
b1 = field_comp("reuters_plaza", "whole", "people", 4400, 700)
b2 = moved(field_comp("reuters_plaza", "plaza3", "people", 4400, 760), 0, 0, 6.0, 4500, 760)
b2b = emit("transformfield", 4600, 760)
wire(b2, 0, b2b, 0)
impl(b2b.Params.Input[1]).PersistentData.Clear()
impl(b2b.Params.Input[1]).PersistentData.Append(GH_Vector(RG.Vector3d(0, 135.0, 0)))
br = emit("bridge", 4800, 720)
impl(br.Params.Input[0]).AddSource(impl(b1.Params.Output[0]))
impl(br.Params.Input[0]).AddSource(impl(b2b.Params.Output[0]))
put(br, 2, 2)
put(br, 5, 120.0)
solve()
bridges = [g_.Value for g_ in out(br, 0)]
all_b = [g_.Value for g_ in out(br, 2)]
ps = ParticleSystem()
for f_ in all_b:
    ps.Fields.Add(f_)
ps.Rate, ps.Life, ps.Seed, ps.Join = 300.0, 200.0, 1, 2.0
first, reached_b = {}, {}
for t_ in range(1, 151):
    ps.StepTo(float(t_))
    for p_ in ps.Alive:
        first.setdefault(p_.Id, p_.Layer)
        reached_b[p_.Id] = reached_b.get(p_.Id, set()) | {p_.Layer}
crossed = sum(1 for i_ in first if first[i_] in (0, 1) and ({0, 1} - {first[i_]}) <= reached_b[i_])
onto_bridge = sum(1 for i_ in first if first[i_] in (0, 1) and any(l_ >= 2 for l_ in reached_b[i_]))
check("Bridge: automatic ribbons between two plaza levels",
      len(bridges) >= 2 and all(b_.Warped for b_ in bridges) and onto_bridge > 20 and crossed > 0,
      f"{len(bridges)} bridges; in 150 s {onto_bridge} particles walked onto a bridge, {crossed} across to the other level")

# Apply Forces: an orbit bends the plaza's flow into a swirl, speeds kept
axf = emit("axis", 4600, 1000)
impl(axf.Params.Input[0]).PersistentData.Clear()
impl(axf.Params.Input[0]).PersistentData.Append(GH_Line(RG.Line(RG.Point3d(pc_x, pc_y, 0), RG.Point3d(pc_x, pc_y, 10))))
put(axf, 2, 40.0)
af = emit("applyforces", 4800, 1000)
wire(pz, 0, af, 0)
impl(af.Params.Input[1]).AddSource(impl(axf.Params.Output[0]))
solve()
f_a = out(af, 0)[0].Value
mom0 = mom1 = 0.0
sp_err = 0.0
for k in range(pz_f.Count):
    if not pz_f.Has[k]:
        continue
    w_ = pz_f.CellWorld(k)
    v0, v1 = pz_f.CellVelocity(k), f_a.CellVelocity(k)
    rx, ry = w_.Item1 - pc_x, w_.Item2 - pc_y
    mom0 += rx * v0.Item2 - ry * v0.Item1
    mom1 += rx * v1.Item2 - ry * v1.Item1
    s0, s1 = math.hypot(v0.Item1, v0.Item2), math.hypot(v1.Item1, v1.Item2)
    sp_err = max(sp_err, abs(s1 - s0))
check("Apply Forces: an Axis Force bends the flow into a swirl", mom1 > mom0 + 100 and sp_err < 1e-4,
      f"angular momentum of the flow about the axis {mom0:.0f} -> {mom1:.0f}; speeds kept (largest change {sp_err:.1e} m/s)")

# Intersect: the plaza and a copy tilted 35 deg about an east-west line through its centre cut along that line
tilt = gh_node("Rotate 3D", pz, 4600, 1100, i1=GH_Number(math.radians(35)), i2=GH_Point(RG.Point3d(pc_x, pc_y, 0)),
               i3=GH_Vector(RG.Vector3d(1, 0, 0)))
cut = emit("intersect", 4800, 1100)
impl(cut.Params.Input[0]).AddSource(impl(pz.Params.Output[0]))
impl(cut.Params.Input[0]).AddSource(impl(tilt.Params.Output[0]))
solve()
cuts = [g_.Value for g_ in out(cut, 0)]
cut_len = sum(c_.GetLength() for c_ in cuts)
off_axis = 0.0
for c_ in cuts:
    ok_, pl_ = c_.TryGetPolyline()
    for q in pl_:
        off_axis = max(off_axis, math.hypot(q.Y - pc_y, q.Z))
check("Intersect Fields: the plaza and a copy tilted 35 deg cut along the hinge line", len(cuts) >= 1 and cut_len > 10 and off_axis < 1.0,
      f"{len(cuts)} cut lines, {cut_len:.1f} m long, at most {off_axis:.2f} m off the hinge line; flow angles {[round(a_.Value, 1) for a_ in out(cut, 1)][:4]}")

# Field Surface: a mesh and outlines, also of a warped field
fs = emit("surface", 4800, 1200)
wf2 = emit("warp", 4600, 1200)
wire(pz, 0, wf2, 0)
wire(wf2, 0, fs, 0)
solve()
mesh_ = out(fs, 0)[0].Value
outl = [g_.Value for g_ in out(fs, 1)]
check("Field Surface: a coloured mesh and outlines of a warped field",
      mesh_.Faces.Count > 1000 and mesh_.VertexColors.Count == mesh_.Vertices.Count and len(outl) >= 1 and outl[0].IsClosed,
      f"{mesh_.Faces.Count} faces, {len(outl)} outlines (longest {outl[0].GetLength():.0f} m)")

# step time on a bent field
ps = ParticleSystem(out(wf2, 0)[0].Value)
ps.Rate, ps.Life, ps.Seed = 300.0, 30.0, 1
ps.StepTo(15.0)
for _ in range(3):
    if len(ps.Alive):
        ps.Rate = ps.Rate * 3000.0 / len(ps.Alive)
        ps.StepTo(0.0)
        ps.StepTo(15.0)
sw = System.Diagnostics.Stopwatch.StartNew()
for _ in range(60):
    ps.Step()
check("Step time on a warped field", len(ps.Alive) > 2000, f"{len(ps.Alive)} particles: {sw.Elapsed.TotalMilliseconds / 60:.1f} ms per step")

# ---- definitions saved with older versions open cleanly (components gained inputs since) ----------
print("\n== Older definitions", flush=True)
clr.AddReference(r"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll")
from GH_IO.Serialization import GH_Archive  # noqa: E402
fixtures = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")
for fx in sorted(f_ for f_ in os.listdir(fixtures) if f_.endswith(".gh")):
    arc = GH_Archive()
    arc.ReadFromFile(os.path.join(fixtures, fx))
    old = GH_Document()
    arc.ExtractObject(old, "Definition")
    io_msgs = [str(m.Message) for m in arc.Messages if str(m.Type) != "info"]
    old.Enabled = True
    for o in map(impl, old.Objects):
        if str(getattr(o, "Name", "")) == "Timeline":
            o.SetTime(10.0, False)
    old.NewSolution(False)
    pcs = [o for o in map(impl, old.Objects) if str(getattr(o, "Name", "")) == "Particles"]
    n_pts = [len(list(impl(o.Params.Output[0]).VolatileData.AllData(True))) for o in pcs]
    wired = all(impl(o.Params.Input[0]).SourceCount > 0 for o in pcs)
    check(f"Opens {fx}", not io_msgs and pcs and wired and all(n > 0 for n in n_pts),
          f"IO messages {io_msgs}; {len(pcs)} Particles, still wired, with {n_pts} particles at 10 s")

# ---- the example file ------------------------------------------------------------------------
if args.example:
    ex = GH_Document()
    sites = Catalog.Sites(results)
    plaza = next(s for s in sites if s.Name == "reuters_plaza")

    def add(obj, x, y):
        obj.CreateAttributes()
        obj.Attributes.Pivot = PointF(x, y)
        ex.AddObject(obj, False)
        return obj

    def comp(key, x, y):
        obj = impl(cs.EmitObject(System.Guid(G[key])))
        if key == "field":
            obj.GetType().GetField("_read", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(obj, True)
        return add(obj, x, y)

    def dropdown(name, items, sel, x, y):
        vl = GH_ValueList()
        vl.NickName = name
        vl.Description = "groundtrack:" + name.lower()
        vl.ListMode = GH_ValueListMode.DropDown
        vl.ListItems.Clear()
        for it in items:
            vl.ListItems.Add(GH_ValueListItem(it, '"%s"' % it))
        vl.SelectItem(items.index(sel) if sel in items else 0)
        return add(vl, x, y)

    def group(name, objs, colour):
        g = GH_Group()
        g.NickName = name
        g.Colour = colour
        for o in objs:
            g.AddObject(o.InstanceGuid)
        add(g, 0, 0)

    def field_with_lists(clip, grp, x, y, site="reuters_plaza"):
        st = next(s_ for s_ in sites if s_.Name == site)
        f_ = comp("field", x, y)
        lists = [dropdown("Site", [s.Name for s in sites], site, x - 210, y - 32),
                 dropdown("Clip", list(st.Clips), clip, x - 210, y - 10),
                 dropdown("Group", [str(k) for k in Catalog.FieldFiles(st, clip).Keys], grp, x - 210, y + 12)]
        for ix, vl in zip((1, 2, 3), lists):
            impl(f_.Params.Input[ix]).AddSource(vl)
        return f_, lists

    move_guid = next(p_.Guid for p_ in cs.ObjectProxies
                     if str(p_.Desc.Name) == "Move" and str(p_.Desc.Category) == "Transform")

    def move(src, dx, x, y):
        """Grasshopper's own Move node: places a field (the particles / replay follow)."""
        m_ = add(impl(cs.EmitObject(move_guid)), x, y)
        m_.Hidden = True
        impl(m_.Params.Input[0]).AddSource(impl(src.Params.Output[0]))
        impl(m_.Params.Input[1]).PersistentData.Append(
            Grasshopper.Kernel.Types.GH_Vector(Rhino.Geometry.Vector3d(dx, 0, 0)))
        return m_

    # 1: Field -> Streamlines (the two-node start)
    f1, l1 = field_with_lists("whole", "people", 260, 80)
    f1.Hidden = True
    s1 = comp("stream", 520, 80)
    impl(s1.Params.Input[0]).AddSource(impl(f1.Params.Output[0]))
    group("1  Field -> Streamlines: pick site / clip / group", l1 + [f1, s1], Color.FromArgb(60, 120, 170, 255))
    # 2: particles + replay on one timeline, moved 70 m east (Move works on fields)
    f2, l2 = field_with_lists("plaza1", "people", 260, 330)
    f2.Hidden = True
    m2 = move(f2, 70.0, 420, 330)
    t2 = comp("timeline", 420, 470)
    p2 = comp("particles", 640, 330)
    r2 = comp("replay", 640, 520)
    for c_ in (p2, r2):
        impl(c_.Params.Input[0]).AddSource(impl(m2.Params.Output[0]))
        impl(c_.Params.Input[1]).AddSource(impl(t2.Params.Output[0]))
    group("2  Particles + Replay on a Timeline (moved 70 m east): double-click the Timeline to play / scrub / record",
          l2 + [f2, m2, t2, p2, r2], Color.FromArgb(60, 255, 170, 60))
    # 3: two clips blended, and where their flows cross, 140 m east
    f3, l3 = field_with_lists("plaza1", "people", 260, 700)
    f4, l4 = field_with_lists("plaza3", "people", 260, 830)
    f3.Hidden = True
    f4.Hidden = True
    m3, m4 = move(f3, 140.0, 420, 700), move(f4, 140.0, 420, 830)
    b3 = comp("blend", 640, 760)
    b3.Hidden = True
    impl(b3.Params.Input[0]).AddSource(impl(m3.Params.Output[0]))
    impl(b3.Params.Input[0]).AddSource(impl(m4.Params.Output[0]))
    impl(b3.Params.Input[1]).PersistentData.Append(GH_Number(0.5))
    s3, s4 = comp("stream", 640, 680), comp("stream", 640, 880)
    impl(s3.Params.Input[0]).AddSource(impl(m3.Params.Output[0]))
    impl(s4.Params.Input[0]).AddSource(impl(m4.Params.Output[0]))
    c3 = comp("cross", 860, 780)
    impl(c3.Params.Input[0]).AddSource(impl(s3.Params.Output[0]))
    impl(c3.Params.Input[1]).AddSource(impl(s4.Params.Output[0]))
    group("3  Two clips: Blend (T slider) and where their flows cross (heat map), 140 m east",
          l3 + l4 + [f3, f4, m3, m4, b3, s3, s4, c3], Color.FromArgb(60, 230, 90, 160))
    # 4: layers + forces - two plaza fields stacked 8 m apart, pulling on each other and swirling, 210 m east
    pf_bounds = Catalog.Load(plaza, "whole", "people", True).Bounds()
    pcx, pcy = (pf_bounds.Item1 + pf_bounds.Item3) / 2 + 210.0, (pf_bounds.Item2 + pf_bounds.Item4) / 2
    f5, l5 = field_with_lists("whole", "people", 260, 1100)
    f6, l6 = field_with_lists("plaza3", "people", 260, 1230)
    f5.Hidden = True
    f6.Hidden = True
    tf5, tf6 = comp("transformfield", 420, 1100), comp("transformfield", 420, 1230)
    for tf_, src_, dz in ((tf5, f5, 0.0), (tf6, f6, 8.0)):
        tf_.Hidden = True
        impl(tf_.Params.Input[0]).AddSource(impl(src_.Params.Output[0]))
        impl(tf_.Params.Input[1]).PersistentData.Clear()
        impl(tf_.Params.Input[1]).PersistentData.Append(Grasshopper.Kernel.Types.GH_Vector(Rhino.Geometry.Vector3d(210.0, 0.0, dz)))
    it4 = comp("interact", 420, 1360)
    for ix, v in ((1, 10.0), (3, 1.5)):
        impl(it4.Params.Input[ix]).PersistentData.Clear()
        impl(it4.Params.Input[ix]).PersistentData.Append(GH_Number(v))
    ax4 = comp("axis", 420, 1470)
    impl(ax4.Params.Input[0]).PersistentData.Clear()
    impl(ax4.Params.Input[0]).PersistentData.Append(Grasshopper.Kernel.Types.GH_Line(
        Rhino.Geometry.Line(Rhino.Geometry.Point3d(pcx, pcy, 0), Rhino.Geometry.Point3d(pcx, pcy, 10))))
    t4 = comp("timeline", 640, 1330)
    p4 = comp("particles", 860, 1180)
    impl(p4.Params.Input[0]).AddSource(impl(tf5.Params.Output[0]))
    impl(p4.Params.Input[0]).AddSource(impl(tf6.Params.Output[0]))
    impl(p4.Params.Input[1]).AddSource(impl(t4.Params.Output[0]))
    impl(p4.Params.Input[10]).AddSource(impl(it4.Params.Output[0]))
    impl(p4.Params.Input[10]).AddSource(impl(ax4.Params.Output[0]))
    for ix, goo in ((4, GH_Number(200.0)), (12, GH_Number(0.2)), (15, GH_String("layer"))):
        impl(p4.Params.Input[ix]).PersistentData.Clear()
        impl(p4.Params.Input[ix]).PersistentData.Append(goo)
    group("4  Layers + forces: two fields stacked 8 m apart (Transform Field), pulling on each other (Interact) and swirling (Axis Force)",
          l5 + l6 + [f5, f6, tf5, tf6, it4, ax4, t4, p4], Color.FromArgb(60, 140, 90, 255))

    # 5: tilted - the plaza stood up on its south edge with Grasshopper's Rotate 3D, 420 m east
    f7, l7 = field_with_lists("whole", "people", 260, 1650)
    f7.Hidden = True
    r7 = add(impl(cs.EmitObject(names_["Rotate 3D"])), 420, 1650)
    r7.Hidden = True
    impl(r7.Params.Input[0]).AddSource(impl(f7.Params.Output[0]))
    for ix, goo in ((1, GH_Number(math.pi / 2)), (2, GH_Point(RG.Point3d(0, pf_bounds.Item2, 0))),
                    (3, GH_Vector(RG.Vector3d(1, 0, 0)))):
        prm = impl(r7.Params.Input[ix])
        prm.PersistentData.Clear()
        prm.PersistentData.Append(goo)
    m7 = move(r7, 420.0, 580, 1650)
    s7 = comp("stream", 760, 1600)
    impl(s7.Params.Input[0]).AddSource(impl(m7.Params.Output[0]))
    t7 = comp("timeline", 580, 1760)
    p7 = comp("particles", 760, 1720)
    impl(p7.Params.Input[0]).AddSource(impl(m7.Params.Output[0]))
    impl(p7.Params.Input[1]).AddSource(impl(t7.Params.Output[0]))
    group("5  Tilted: the plaza stood up on its south edge with Grasshopper's Rotate 3D (90 deg about X); "
          "streamlines and particles run up the wall", l7 + [f7, r7, m7, s7, t7, p7], Color.FromArgb(60, 240, 170, 60))

    # 6: two levels joined by drawn ramps, merged into one continuous field - 560 m east
    off6 = 560.0
    f8, l8 = field_with_lists("whole", "people", 260, 1950)
    f9, l9 = field_with_lists("plaza3", "people", 260, 2080)
    f8.Hidden = f9.Hidden = True
    tf8, tf9 = comp("transformfield", 420, 1950), comp("transformfield", 420, 2080)
    for tf_, src_, mv in ((tf8, f8, RG.Vector3d(off6, 0, 0)),
                          (tf9, f9, RG.Vector3d(off6, lb_.Item4 + RAMP_GAP - hb_.Item2, RAMP_RISE))):
        tf_.Hidden = True
        impl(tf_.Params.Input[0]).AddSource(impl(src_.Params.Output[0]))
        impl(tf_.Params.Input[1]).PersistentData.Clear()
        impl(tf_.Params.Input[1]).PersistentData.Append(GH_Vector(mv))
    rp6 = add(Grasshopper.Kernel.Parameters.Param_Curve(), 420, 2200)
    rp6.NickName = "Ramps"
    rp6.Hidden = True
    for c_ in ramps:
        c2 = c_.DuplicateCurve()
        c2.Translate(RG.Vector3d(off6, 0, 0))
        rp6.PersistentData.Append(GH_Curve(c2))
    cf6 = comp("curvefield", 560, 2200)
    cf6.Hidden = True
    impl(cf6.Params.Input[0]).AddSource(rp6)
    mg6 = comp("merge", 700, 2050)
    mg6.Hidden = True
    for src_ in (tf8, tf9, cf6):
        impl(mg6.Params.Input[0]).AddSource(impl(src_.Params.Output[0]))
    s6 = comp("stream", 880, 1990)
    impl(s6.Params.Input[0]).AddSource(impl(mg6.Params.Output[0]))
    t6 = comp("timeline", 700, 2180)
    p6 = comp("particles", 880, 2120)
    impl(p6.Params.Input[0]).AddSource(impl(mg6.Params.Output[0]))
    impl(p6.Params.Input[1]).AddSource(impl(t6.Params.Output[0]))
    group("6  Ramps between levels: the plaza, and plaza 3 30 m north and 5 m up (Transform Field), joined by two drawn ramps "
          "(Ramps curves -> Field from Curves) and merged into one continuous field (Merge Fields)",
          l8 + l9 + [f8, f9, tf8, tf9, rp6, cf6, mg6, s6, t6, p6], Color.FromArgb(60, 90, 200, 230))

    # 7: a folded surface - three copies hinged edge to edge: floor, 30 deg slope, wall - 700 m east
    f10, l10 = field_with_lists("motorway5", "vehicles", 260, 2400, site="blackwall_motorway")
    f10.Hidden = True
    sm10 = comp("smoothfield", 400, 2400)
    sm10.Hidden = True
    impl(sm10.Params.Input[0]).AddSource(impl(f10.Params.Output[0]))
    for ix, goo in ((1, GH_Number(4.0)), (2, GH_Boolean(True))):
        impl(sm10.Params.Input[ix]).PersistentData.Clear()
        impl(sm10.Params.Input[ix]).PersistentData.Append(goo)
    chain7 = []
    ends7 = [gh_chain(add, sm10, st_, 560, 2340 + 70 * i, chain7) for i, st_ in enumerate(fold_steps(700.0, fold_base))]
    s7b = comp("stream", 1060, 2330)
    t7b = comp("timeline", 880, 2560)
    p7b = comp("particles", 1060, 2460)
    for e_ in ends7:
        impl(s7b.Params.Input[0]).AddSource(impl(e_.Params.Output[0]))
        impl(p7b.Params.Input[0]).AddSource(impl(e_.Params.Output[0]))
    impl(p7b.Params.Input[1]).AddSource(impl(t7b.Params.Output[0]))
    for ix, goo in ((15, GH_String("layer")), (17, GH_Number(FOLD_JOIN))):
        impl(p7b.Params.Input[ix]).PersistentData.Clear()
        impl(p7b.Params.Input[ix]).PersistentData.Append(goo)
    group("7  Folded surface: a motorway clip (Smooth Field: holes closed) three times, hinged edge to edge - floor, 30 deg "
          "slope, wall (Move + Rotate 3D about the shared edges); vehicles carry on from one onto the next (Particles: Join 8 m)",
          l10 + [f10, sm10] + chain7 + [s7b, t7b, p7b], Color.FromArgb(60, 230, 120, 160))

    # 8: a lofted landscape - three plaza layers stacked 8 m apart, the top one waving, joined at their edges
    off8 = 900.0
    G["loft"] = "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e522"
    G["warp"] = "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e520"
    G["surface"] = "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e526"
    G["bridge"] = "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e523"
    G["intersect"] = "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e525"
    lay8, objs8 = [], []
    for i_, (clip_, dz_) in enumerate((("whole", 16.0), ("plaza3", 8.0), ("plaza1", 0.0))):
        f_, l_ = field_with_lists(clip_, "people", 260, 2700 + 110 * i_)
        f_.Hidden = True
        tf_ = comp("transformfield", 420, 2700 + 110 * i_)
        tf_.Hidden = True
        impl(tf_.Params.Input[0]).AddSource(impl(f_.Params.Output[0]))
        impl(tf_.Params.Input[1]).PersistentData.Clear()
        impl(tf_.Params.Input[1]).PersistentData.Append(GH_Vector(RG.Vector3d(off8, 0, dz_)))
        lay8.append(tf_)
        objs8 += l_ + [f_, tf_]
    wv8 = comp("warp", 580, 2700)
    wv8.Hidden = True
    impl(wv8.Params.Input[0]).AddSource(impl(lay8[0].Params.Output[0]))
    lf8 = comp("loft", 760, 2800)
    lf8.Hidden = True
    for src_ in (wv8, lay8[1], lay8[2]):
        impl(lf8.Params.Input[0]).AddSource(impl(src_.Params.Output[0]))
    s8 = comp("stream", 960, 2740)
    impl(s8.Params.Input[0]).AddSource(impl(lf8.Params.Output[1]))
    t8 = comp("timeline", 780, 2960)
    p8 = comp("particles", 960, 2880)
    impl(p8.Params.Input[0]).AddSource(impl(lf8.Params.Output[1]))
    impl(p8.Params.Input[1]).AddSource(impl(t8.Params.Output[0]))
    sf8 = comp("surface", 960, 3000)
    impl(sf8.Params.Input[0]).AddSource(impl(lf8.Params.Output[1]))
    group("8  Lofted landscape: three plaza layers stacked 8 m apart (Transform Field), the top one waving (Warp Field), "
          "joined at their edges by rounded folds (Loft Fields): streamlines, particles and surfaces over all of it",
          objs8 + [wv8, lf8, s8, t8, p8, sf8], Color.FromArgb(60, 120, 200, 150))

    # 9: bend, bridge and cut - plaza 3 bent up and joined to the plaza by ribbons; a tilted copy cutting the plaza
    off9 = 1100.0
    f11, l11 = field_with_lists("whole", "people", 260, 3250)
    f12, l12 = field_with_lists("plaza3", "people", 260, 3380)
    f11.Hidden = f12.Hidden = True
    tf11, tf12 = comp("transformfield", 420, 3250), comp("transformfield", 420, 3380)
    for tf_, src_, mv in ((tf11, f11, RG.Vector3d(off9, 0, 0)), (tf12, f12, RG.Vector3d(off9, 135.0, 6.0))):
        tf_.Hidden = True
        impl(tf_.Params.Input[0]).AddSource(impl(src_.Params.Output[0]))
        impl(tf_.Params.Input[1]).PersistentData.Clear()
        impl(tf_.Params.Input[1]).PersistentData.Append(GH_Vector(mv))
    bd12 = comp("warp", 580, 3380)
    bd12.Hidden = True
    impl(bd12.Params.Input[0]).AddSource(impl(tf12.Params.Output[0]))
    for ix, goo in ((1, GH_String("bend")), (2, GH_Number(60.0))):
        impl(bd12.Params.Input[ix]).PersistentData.Clear()
        impl(bd12.Params.Input[ix]).PersistentData.Append(goo)
    br9 = comp("bridge", 760, 3300)
    br9.Hidden = True
    impl(br9.Params.Input[0]).AddSource(impl(tf11.Params.Output[0]))
    impl(br9.Params.Input[0]).AddSource(impl(bd12.Params.Output[0]))
    for ix, goo in ((2, GH_Integer(2)), (5, GH_Number(120.0))):
        impl(br9.Params.Input[ix]).PersistentData.Clear()
        impl(br9.Params.Input[ix]).PersistentData.Append(goo)
    s9 = comp("stream", 960, 3260)
    impl(s9.Params.Input[0]).AddSource(impl(br9.Params.Output[2]))
    t9 = comp("timeline", 780, 3470)
    p9 = comp("particles", 960, 3390)
    impl(p9.Params.Input[0]).AddSource(impl(br9.Params.Output[2]))
    impl(p9.Params.Input[1]).AddSource(impl(t9.Params.Output[0]))
    tl9 = add(impl(cs.EmitObject(names_["Rotate 3D"])), 580, 3520)
    tl9.Hidden = True
    impl(tl9.Params.Input[0]).AddSource(impl(tf11.Params.Output[0]))
    b11 = Catalog.Load(plaza, "whole", "people", True).Bounds()
    for ix, goo in ((1, GH_Number(math.radians(35))), (2, GH_Point(RG.Point3d(off9 + (b11.Item1 + b11.Item3) / 2, (b11.Item2 + b11.Item4) / 2, 0))),
                    (3, GH_Vector(RG.Vector3d(1, 0, 0)))):
        impl(tl9.Params.Input[ix]).PersistentData.Clear()
        impl(tl9.Params.Input[ix]).PersistentData.Append(goo)
    ct9 = comp("intersect", 760, 3520)
    impl(ct9.Params.Input[0]).AddSource(impl(tf11.Params.Output[0]))
    impl(ct9.Params.Input[0]).AddSource(impl(tl9.Params.Output[0]))
    s9b = comp("stream", 960, 3540)
    impl(s9b.Params.Input[0]).AddSource(impl(tl9.Params.Output[0]))
    group("9  Bend, bridge, cut: plaza 3 bent up 60 deg (Warp Field) and joined to the plaza by automatic ribbons (Bridge); "
          "a copy tilted 35 deg cuts the plaza (Intersect Fields)",
          l11 + l12 + [f11, f12, tf11, tf12, bd12, br9, s9, t9, p9, tl9, ct9, s9b], Color.FromArgb(60, 200, 160, 230))
    ex.NewSolution(False)
    bad = [(o.NickName, [str(m) for m in o.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)])
           for o in map(impl, ex.Objects) if hasattr(o, "RuntimeMessages") and
           list(o.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error))]
    io = GH_DocumentIO(ex)
    saved = io.SaveQuiet(args.example)
    # read it back and solve
    io2 = GH_DocumentIO()
    io2.Open(args.example)
    ex2 = io2.Document
    ex2.Enabled = True
    ex2.NewSolution(False)
    bad2 = [o.NickName for o in map(impl, ex2.Objects) if hasattr(o, "RuntimeMessages") and
            list(o.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error))]
    streams = [o for o in map(impl, ex2.Objects) if o.ComponentGuid == System.Guid(G["stream"])]
    nlines = sum(len(list(impl(o.Params.Output[0]).VolatileData.AllData(True))) for o in streams)
    check("Example .gh", saved and not bad and not bad2 and nlines > 0,
          f"saved {args.example} ({os.path.getsize(args.example) // 1024} kB), {ex2.ObjectCount} objects, "
          f"re-opened and solved: {nlines} streamlines, errors {bad + bad2}")

print("\nPASS" if not FAIL else f"\nFAIL: {FAIL}", flush=True)
sys.stdout.flush()
os._exit(0 if not FAIL else 1)
