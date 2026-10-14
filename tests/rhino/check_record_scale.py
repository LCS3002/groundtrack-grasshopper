"""Starts a visible Rhino 8 with gui_record_scale_test.py and measures its frames: Record at 2x
must give frames twice the size with the lines twice as thick (the coloured share of the frame
stays the same), and a larger Width must draw thicker streamlines.

    python check_record_scale.py <example.gh> <work folder>

Rhino closes itself at the end. Ends with PASS or FAIL.
"""
import json
import os
import subprocess
import sys
import time

from PIL import Image

example, work = sys.argv[1], sys.argv[2]
frames = os.path.join(work, "frames")
result = os.path.join(work, "result.json")
os.makedirs(frames, exist_ok=True)
if os.path.exists(result):
    os.remove(result)
here = os.path.dirname(os.path.abspath(__file__))
env = dict(os.environ, GT_EXAMPLE=example, GT_FRAMES=frames, GT_RESULT=result)
script = os.path.join(here, "gui_record_scale_test.py")   # Rhino's /runscript takes no quotes inside
assert " " not in script, "the test script's path must not contain spaces"
rhino = subprocess.Popen(r'"C:\Program Files\Rhino 8\System\Rhino.exe" /nosplash '
                         f'/runscript="_-RunPythonScript {script}"', env=env)
t0 = time.time()
while time.time() - t0 < 300 and rhino.poll() is None:
    time.sleep(2)
if rhino.poll() is None:
    print("Rhino did not close within 300 s")
res = json.load(open(result)) if os.path.exists(result) else {"error": "no result file"}
print(json.dumps(res, indent=1))


def coloured(path):
    """Share of the frame drawn in colour (groundtrack's lines; the viewport itself is grey)."""
    im = Image.open(path).convert("RGB")
    n = sum(1 for r, g, b in im.getdata() if max(r, g, b) - min(r, g, b) > 50)
    return im.size, n


fail = ["error" in res]
m = {}
for case in res.get("cases", []):
    d = os.path.join(frames, case["name"])
    pngs = sorted(f for f in os.listdir(d) if f.endswith(".png")) if os.path.isdir(d) else []
    if not pngs:
        print(case["name"], "no frames")
        fail.append(True)
        continue
    m[case["name"]] = coloured(os.path.join(d, pngs[0]))
    (w, h), n = m[case["name"]]
    print(f"{case['name']}: {len(pngs)} frames, {w}x{h}, coloured {n} px = {n / (w * h):.4f} of the frame")
if len(m) == 3:
    (w1, h1), n1 = m["scale1_w1.5"]
    (w2, h2), n2 = m["scale2_w1.5"]
    _, n4 = m["scale1_w4"]
    vp = res.get("viewport", [0, 0])
    size_ok = (w1, h1) == tuple(vp) and (w2, h2) == (2 * w1, 2 * h1)
    thick = n2 / n1          # 4 if the lines grow with the frame, 2 if they stayed thin
    width = n4 / n1
    print(f"size: viewport {vp}, 1x {w1}x{h1}, 2x {w2}x{h2} -> {'ok' if size_ok else 'FAIL'}")
    print(f"coloured pixels 2x / 1x = {thick:.2f} (4 = lines scaled with the frame, 2 = thin) -> "
          f"{'ok' if 3.2 < thick < 4.8 else 'FAIL'}")
    print(f"coloured pixels Width 4 / Width 1.5 = {width:.2f} (> 1.3: thicker streamlines) -> "
          f"{'ok' if width > 1.3 else 'FAIL'}")
    fail += [not size_ok, not 3.2 < thick < 4.8, not width > 1.3]
else:
    fail.append(True)
print("FAIL" if any(fail) else "PASS")
