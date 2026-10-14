#! python3
"""Runs INSIDE a visible Rhino 8 (Rhino.exe /runscript): the timeline's Record at 1x and 2x the
viewport, and the Width input, on the example's plaza streamlines. Writes the frames and a result
file; check_record_scale.py measures them.

    Rhino.exe /nosplash /runscript="_-RunPythonScript <this file>"

Settings come from environment variables: GT_EXAMPLE (the .gh), GT_FRAMES (folder for the
frames, one subfolder per case), GT_RESULT (result json). Rhino exits when done.
"""
import json
import os

import Rhino
import scriptcontext as sc

example = os.environ["GT_EXAMPLE"]
frames = os.environ["GT_FRAMES"]
result_path = os.environ["GT_RESULT"]
result = {"steps": [], "cases": []}
# (folder, record scale, streamline width)
CASES = [("scale1_w1.5", 1, 1.5), ("scale2_w1.5", 2, 1.5), ("scale1_w4", 1, 4.0)]


def impl(o):
    return getattr(o, "__implementation__", o)


def done(extra):
    result.update(extra)
    with open(result_path, "w") as fh:
        json.dump(result, fh, indent=1)


try:
    from Grasshopper.Kernel.Types import GH_Number
    gh = Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    gh.LoadEditor()
    gh.ShowEditor()
    gh.OpenDocument(example)
    import Grasshopper
    doc = Grasshopper.Instances.ActiveCanvas.Document
    doc.NewSolution(False)
    objs = [impl(o) for o in doc.Objects]
    tl = next(o for o in objs if o.GetType().Name == "TimelineComponent")
    streams = [o for o in objs if o.GetType().Name == "StreamlinesComponent" and o.ClippingBox.IsValid]
    stream = min(streams, key=lambda o: o.ClippingBox.Center.X)       # group 1, the plaza
    box = stream.ClippingBox
    view = sc.doc.Views.Find("Top", False)
    sc.doc.Views.ActiveView = view
    view.Maximized = True
    view.ActiveViewport.ZoomBoundingBox(box)
    view.Redraw()
    size = view.ActiveViewport.Size
    result["viewport"] = [size.Width, size.Height]
    result["steps"].append(f"{len(streams)} streamlines components; framed the one at x {box.Center.X:.0f}")
    tl.In, tl.Out, tl.Fps = 0.0, 1.0 / 30.0, 30.0
    width_param = impl(stream.Params.Input[5])
    result["width_input"] = str(width_param.Name)
    queue = list(CASES)

    def start_next():
        name, scale, width = queue.pop(0)
        width_param.PersistentData.Clear()
        width_param.PersistentData.Append(GH_Number(width))
        width_param.ExpireSolution(False)
        stream.ExpireSolution(False)
        tl.RecordScale = scale
        tl.StartRecording(os.path.join(frames, name))
        result["cases"].append({"name": name, "scale": scale, "width": width})

    def on_idle(sender, e):
        if tl.Recording:
            return
        if result["cases"]:
            result["cases"][-1]["frames"] = tl.RecordedFrames
        if queue:
            start_next()
            return
        Rhino.RhinoApp.Idle -= on_idle
        done({"ok": True})
        Grasshopper.Instances.DocumentServer.RemoveDocument(doc)
        sc.doc.Modified = False
        Rhino.RhinoApp.RunScript("_-Exit", False)

    Rhino.RhinoApp.Idle += on_idle
except Exception as ex:  # report, do not hang
    done({"error": repr(ex)})
