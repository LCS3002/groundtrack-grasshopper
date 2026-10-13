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
     "replay": "3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e507"}


def check(name, ok, detail):
    print(f"  [{'ok' if ok else 'FAIL'}] {name}: {detail}", flush=True)
    if not ok:
        FAIL.append(name)


cs = Grasshopper.Instances.ComponentServer
ours = [p for p in cs.ObjectProxies if str(p.Desc.Category) == "groundtrack"]
print(f"Rhino {Rhino.RhinoApp.Version} ({args.fw}), Grasshopper headless; groundtrack components: "
      f"{sorted(str(p.Desc.Name) for p in ours)}", flush=True)
check("plugin loaded", len(ours) >= 7, f"{len(ours)} components on the groundtrack tab")
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
