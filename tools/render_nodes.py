"""Grasshopper draws every groundtrack component itself (normal look, icons), each on black and on
white, at 4x zoom: the pair gives exact transparency later."""
import json
import os
import sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
import rhinoinside

rhinoinside.load(r"C:\Program Files\Rhino 8\System", "net8.0")
import clr  # noqa: E402
import System  # noqa: E402
import Rhino  # noqa: E402

Rhino.RhinoApp.GetPlugInObject("Grasshopper").RunHeadless()
clr.AddReference(r"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll")
clr.AddReference("System.Drawing")
import Grasshopper  # noqa: E402
from Grasshopper.Kernel import GH_Document  # noqa: E402
from Grasshopper.GUI.Canvas import GH_Canvas, GH_Viewport, GH_Skin  # noqa: E402
from System.Drawing import PointF, Color  # noqa: E402


def impl(o):
    return getattr(o, "__implementation__", o)


cs = Grasshopper.Instances.ComponentServer
doc = GH_Document()
doc.Enabled = False          # never solved: every node in its normal state
objs = []
x = 0
for p in cs.ObjectProxies:
    if str(p.Desc.Category) != "groundtrack" or p.Kind != Grasshopper.Kernel.GH_ObjectType.CompiledObject:
        continue
    o = impl(cs.EmitObject(p.Guid))
    if not hasattr(o, "Params"):
        continue
    if o.GetType().Name == "FieldComponent":
        o.GetType().GetField("_read", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(o, True)
    o.CreateAttributes()
    o.IconDisplayMode = Grasshopper.Kernel.GH_IconDisplayMode.icon
    o.Attributes.Pivot = PointF(x, 0)
    doc.AddObject(o, False)
    o.Attributes.ExpireLayout()
    o.Attributes.PerformLayout()
    objs.append(o)
    x += 400

canvas = GH_Canvas()
canvas.Document = doc
grid, edge, shade = GH_Skin.canvas_grid, GH_Skin.canvas_edge, GH_Skin.canvas_shade
GH_Skin.canvas_grid = Color.FromArgb(0, 0, 0, 0)
GH_Skin.canvas_edge = Color.FromArgb(0, 0, 0, 0)
GH_Skin.canvas_shade = Color.FromArgb(0, 0, 0, 0)
ZOOM, M = 4.0, 14.0
meta = []
try:
    for o in objs:
        o.ClearRuntimeMessages()
        for prm in list(o.Params.Input) + list(o.Params.Output):
            impl(prm).ClearRuntimeMessages()
        o.Message = None


        o.Attributes.ExpireLayout()
        o.Attributes.PerformLayout()
        b = o.Attributes.Bounds
        w, h = b.Width + 2 * M, b.Height + 2 * M
        vp = GH_Viewport()
        vp.Width = int(round(w * ZOOM))
        vp.Height = int(round(h * ZOOM))
        vp.Zoom = ZOOM
        vp.Tx = int(round(-(b.X - M) * ZOOM))
        vp.Ty = int(round(-(b.Y - M) * ZOOM))
        name = str(o.Name)
        for bg, tag in ((Color.Black, "black"), (Color.White, "white")):
            bmp = canvas.GenerateHiResImageTile(vp, bg)
            bmp.Save(os.path.join(OUT, f"{name}_{tag}.png"))
        meta.append(dict(name=name, w=vp.Width, h=vp.Height))

finally:
    GH_Skin.canvas_grid, GH_Skin.canvas_edge, GH_Skin.canvas_shade = grid, edge, shade
json.dump(meta, open(os.path.join(OUT, "meta.json"), "w"), indent=1)
print("RENDERED", len(meta))
sys.stdout.flush()
os._exit(0)
