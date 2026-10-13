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

    def field_with_lists(clip, grp, x, y):
        f_ = comp("field", x, y)
        lists = [dropdown("Site", [s.Name for s in sites], "reuters_plaza", x - 210, y - 32),
                 dropdown("Clip", list(plaza.Clips), clip, x - 210, y - 10),
                 dropdown("Group", [str(k) for k in Catalog.FieldFiles(plaza, clip).Keys], grp, x - 210, y + 12)]
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
