"""Install the built plugin into Grasshopper's Libraries folder.

    python tools/install.py [net7.0-windows|net48]

Rhino 8 runs on .NET 8 by default (the net7.0-windows build); in .NET Framework mode
(Rhino started with /netfx) use the net48 build.
"""
import os
import shutil
import sys
from pathlib import Path

fw = sys.argv[1] if len(sys.argv) > 1 else "net7.0-windows"
root = Path(__file__).resolve().parents[1]
src = root / "src" / "Groundtrack" / "bin" / "Release" / fw / "groundtrack.gha"
dst = Path(os.environ["APPDATA"]) / "Grasshopper" / "Libraries" / "groundtrack"
dst.mkdir(parents=True, exist_ok=True)
shutil.copy2(src, dst / "groundtrack.gha")
print(f"installed {src} -> {dst / 'groundtrack.gha'}")
