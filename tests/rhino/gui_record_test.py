#! python3
"""Runs INSIDE a visible Rhino 8 (Rhino.exe /runscript): opens the example definition, opens
the timeline window, records 1 s of the plaza particles + replay to PNGs, writes a result file.

    Rhino.exe /nosplash /runscript="_-RunPythonScript <this file>"

Settings come from environment variables: GT_EXAMPLE (the .gh), GT_FRAMES (folder for the
PNGs), GT_RESULT (result json).
"""
import json
import os

import Rhino
import scriptcontext as sc
import System

example = os.environ["GT_EXAMPLE"]
frames = os.environ["GT_FRAMES"]
result_path = os.environ["GT_RESULT"]
result = {"steps": []}


def impl(o):
    return getattr(o, "__implementation__", o)


def done(extra):
    result.update(extra)
    with open(result_path, "w") as fh:
        json.dump(result, fh, indent=1)


try:
    gh = Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    gh.LoadEditor()
    gh.ShowEditor()
    gh.OpenDocument(example)
    import Grasshopper
    doc = Grasshopper.Instances.ActiveCanvas.Document
    result["steps"].append(f"opened {os.path.basename(example)}: {doc.ObjectCount} objects")
    doc.NewSolution(False)
    objs = [impl(o) for o in doc.Objects]
    timelines = [o for o in objs if o.GetType().Name == "TimelineComponent"]
    particles = [o for o in objs if o.GetType().Name == "ParticlesComponent"]
    replay = [o for o in objs if o.GetType().Name == "ReplayComponent"]
    tl = timelines[0]
    result["steps"].append(f"{len(timelines)} timeline, {len(particles)} particles, {len(replay)} replay components")
    # frame the plaza in the Top view
    box = particles[0].ClippingBox
    for o in objs:                      # everything the example shows
        if hasattr(o, "ClippingBox") and getattr(o, "IsPreviewCapable", False) and not getattr(o, "Hidden", True):
            b = o.ClippingBox
            if b.IsValid:
                box.Union(b)
    view = sc.doc.Views.Find("Top", False)
    sc.doc.Views.ActiveView = view
    view.Maximized = True
    view.ActiveViewport.ZoomBoundingBox(box)
    view.Redraw()
    # the window
    tl.ShowWindow()
    win = tl.GetType().GetField("_window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(tl)
    result["window"] = {"title": str(win.Title), "visible": bool(win.Visible)}
    # record 0..1 s at 30 fps
    tl.In = 0.0
    tl.Out = 1.0
    tl.Fps = 30.0
    tl.StartRecording(frames)
    result["steps"].append("recording started")

    def on_idle(sender, e):
        if tl.Recording:
            return
        Rhino.RhinoApp.Idle -= on_idle
        files = sorted(f for f in os.listdir(frames) if f.endswith(".png"))
        done({"frames": len(files), "first": files[:2], "last": files[-1:], "recorded": tl.RecordedFrames,
              "particles_at_end": len(list(particles[0].Params.Output[0].VolatileData.AllData(True)))})

    Rhino.RhinoApp.Idle += on_idle
except Exception as ex:  # report, do not hang
    done({"error": repr(ex)})
