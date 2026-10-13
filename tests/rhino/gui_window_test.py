#! python3
"""Runs INSIDE a visible Rhino 8: the timeline window can be closed mid-animation and opened
again (a new window, the animation still running), then paused.

    Rhino.exe /nosplash /runscript="_-RunPythonScript <this file>"   (env: GT_EXAMPLE, GT_RESULT)
"""
import json
import os
import time

import Rhino

out = os.environ["GT_RESULT"]
res = {}


def impl(o):
    return getattr(o, "__implementation__", o)


try:
    gh = Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    gh.LoadEditor()
    gh.ShowEditor()
    gh.OpenDocument(os.environ["GT_EXAMPLE"])
    import Grasshopper
    doc = Grasshopper.Instances.ActiveCanvas.Document
    doc.NewSolution(False)
    tl = next(impl(o) for o in doc.Objects if impl(o).GetType().Name == "TimelineComponent")
    tl.ShowWindow()
    first = tl.Window
    tl.Play()
    t0 = time.time()
    state = {"step": 0}

    def idle(s, e):
        el = time.time() - t0
        if state["step"] == 0 and el > 2:          # mid-animation: close the window
            res["time_before_close"] = round(tl.Current, 2)
            first.Close()
            state["step"] = 1
        elif state["step"] == 1 and el > 3.5:      # open it again
            res["playing_after_close"] = bool(tl.Playing)
            tl.ShowWindow()
            second = tl.Window
            res["reopened"] = second is not None and bool(second.Visible)
            res["new_window"] = second is not None and not second.Equals(first)
            state["step"] = 2
            state["t"] = tl.Current
        elif state["step"] == 2 and el > 5:        # still animating, then pause
            res["time_moves_after_reopen"] = tl.Current != state["t"]
            tl.Pause()
            res["paused"] = not tl.Playing
            tl.Window.Close()
            tl.ShowWindow()                        # and once more after a normal close
            res["reopened_again"] = bool(tl.Window.Visible)
            Rhino.RhinoApp.Idle -= idle
            json.dump(res, open(out, "w"), indent=1)

    Rhino.RhinoApp.Idle += idle
except Exception as ex:
    res["error"] = repr(ex)
    json.dump(res, open(out, "w"), indent=1)
