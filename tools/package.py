"""Build both targets and package them.

    python tools/package.py

dist/
  groundtrack-grasshopper-<version>.zip    net7.0-windows/groundtrack.gha (Rhino 8, default .NET),
                                           net48/groundtrack.gha (Rhino 8 started with /netfx),
                                           groundtrack_example.gh, INSTALL.txt
  groundtrack-<version>-rh8_*.yak          the Rhino package (Yak) with both builds
"""
import re
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJ = ROOT / "src" / "Groundtrack"
DIST = ROOT / "dist"
YAK = Path(r"C:\Program Files\Rhino 8\System\Yak.exe")
VERSION = re.search(r"<Version>([^<]+)</Version>", (PROJ / "Groundtrack.csproj").read_text()).group(1)

subprocess.run(["dotnet", "build", str(PROJ), "-c", "Release"], check=True)
if DIST.exists():
    shutil.rmtree(DIST)
DIST.mkdir()
builds = {"net7.0-windows": PROJ / "bin" / "Release" / "net7.0-windows" / "groundtrack.gha",
          "net48": PROJ / "bin" / "Release" / "net48" / "groundtrack.gha"}

install = """groundtrack for Grasshopper {v}

Install (Rhino 8):
  1. Close Rhino.
  2. Copy net7.0-windows\\groundtrack.gha to %APPDATA%\\Grasshopper\\Libraries\\groundtrack\\
     (Rhino started in .NET Framework mode, /netfx: use net48\\groundtrack.gha instead).
  3. If Windows blocked the download: right-click the .gha > Properties > Unblock.
  4. Start Rhino and Grasshopper: the "groundtrack" tab appears.
Or install the .yak: "C:\\Program Files\\Rhino 8\\System\\Yak.exe" install groundtrack-{v}-*.yak

Then open groundtrack_example.gh. The Field component finds a folder called
"GROUNDTRACK RESULTS" under Documents by itself (or connect yours to Folder).
""".format(v=VERSION)

with zipfile.ZipFile(DIST / f"groundtrack-grasshopper-{VERSION}.zip", "w", zipfile.ZIP_DEFLATED) as z:
    for fw, gha in builds.items():
        z.write(gha, f"{fw}/groundtrack.gha")
    z.write(ROOT / "examples" / "groundtrack_example.gh", "groundtrack_example.gh")
    z.writestr("INSTALL.txt", install)

# the Yak package: both builds in framework folders
stage = DIST / "yak"
stage.mkdir()
for fw, gha in builds.items():
    (stage / fw).mkdir()
    shutil.copy2(gha, stage / fw / "groundtrack.gha")
shutil.copy2(PROJ / "Icons" / "field.png", stage / "icon.png")
(stage / "manifest.yml").write_text(f"""---
name: groundtrack
version: {VERSION}
authors:
- LCS3002
description: >
  Movement data from groundtrack (video tracking) in Grasshopper - fields, streamlines,
  particles (POP-like), blending, crossings, replay of the real tracks and a timeline.
url: https://github.com/LCS3002/groundtrack-grasshopper
icon: icon.png
keywords:
- movement
- flow
- particles
- streamlines
- tracking
""")
if YAK.exists():
    subprocess.run([str(YAK), "build", "--platform", "win"], cwd=stage, check=True)
    for y in stage.glob("*.yak"):
        shutil.move(str(y), DIST / y.name)
shutil.rmtree(stage)
print("dist:", sorted(p.name for p in DIST.iterdir()))
