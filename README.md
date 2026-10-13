# groundtrack for Grasshopper

Movement data in Grasshopper (Rhino 8): flow fields, streamlines, a particle system like Houdini's
POP network (with forces, layers and collisions), merging and blending, crossings, replay of the
real tracks, and a timeline with recording.

**Quick guide:** [docs/GUIDE.md](docs/GUIDE.md). It covers every node, the Houdini POP
equivalents, and recipes.

It reads the results of [groundtrack](https://github.com/LCS3002/groundtrack), which turns video of
people and vehicles into tracks and flow fields in metres (British National Grid). Every output is
plain Grasshopper geometry: curves, points, vectors and meshes. So Move, Rotate, Curve|Curve,
Tween, Bake and everything else work on it.

![components](docs/components.png)

## Components (tab "groundtrack")

| | Component | What it does |
|---|---|---|
| ![](src/Groundtrack/Icons/field.png) | **Field** | Loads a field from your results. **Site / Clip / Group** come as dropdowns: the whole site (all clips combined) or one clip, and people, vehicles, cycles, buses or all. Optional **Time**: just the movement around that moment, from the tracks. Grasshopper's Move / Rotate / Rotate 3D / Orient / Scale work on it like on geometry. Rotating about a horizontal axis tilts the field or stands it up, and its flow, streamlines, particles and replay go with it. |
| ![](src/Groundtrack/Icons/transformfield.png) | **Transform Field** | Place a field: Rotate in degrees about its own centre, Scale, Stretch X / Y and Mirror, all in the field's own plane, then Move (Z stacks it as a layer). |
| ![](src/Groundtrack/Icons/curvefield.png) | **Field from Curves** | Draw your own flow: it runs along the curves at Speed, fading out over Width. |
| ![](src/Groundtrack/Icons/streamlines.png) | **Streamlines** | Evenly spaced flow lines through a field, coloured by speed. One **Density** slider. |
| ![](src/Groundtrack/Icons/particles.png) | **Particles** | POP-like: particles are born where people or vehicles were (busier places more), follow their field, are pushed by **Forces** and die outside it. **Several fields = several layers.** Other inputs: Rate, Life, Speed, Noise, Trail, Response (inertia), Hold (stick to the layer's ground), Drag, Outside, Colour (speed / layer / age), and **Join**: fields meeting edge to edge (a floor, a slope, a wall) become one surface, and particles carry on from one onto the next, across gaps of up to Join metres. Driven by a Timeline (scrub back and forth), or **Run**. |
| ![](src/Groundtrack/Icons/merge.png) | **Merge Fields** | Several fields into one (sites, clips, groups). No seams: **Feather** (fade in at the edges), **Smooth**, **Fill** gaps, and **Max slope**, which turns cliffs between layers at different heights into ramps. |
| ![](src/Groundtrack/Icons/blend.png) | **Blend Fields** | Interpolate fields, also of different sites. A **T** slider runs from the first field to the last; Weights are optional. The same seam controls as Merge. |
| ![](src/Groundtrack/Icons/smooth.png) | **Smooth Field** | Relax a field: Radius, Fill gaps, Max slope. |
| ![](src/Groundtrack/Icons/force.png) | **Force** | POP Force: a constant push plus turbulence. |
| ![](src/Groundtrack/Icons/axis.png) | **Axis Force** | POP Axis Force: orbit around an axis, suck towards it, lift along it. |
| ![](src/Groundtrack/Icons/attract.png) | **Attract** | POP Attract: towards points, or away from them with a negative Strength. |
| ![](src/Groundtrack/Icons/curveforce.png) | **Curve Force** | Particles follow, are attracted to, or repelled by curves. |
| ![](src/Groundtrack/Icons/fieldforce.png) | **Field Force** | A field's vectors as a force on any layer (wind / POP Advect force mode). Height pull drags particles to that field's ground. |
| ![](src/Groundtrack/Icons/interact.png) | **Interact** | POP Interact / flocking: separation, cohesion and alignment. With Between set to `others`, layers pull on each other and intertwine. |
| ![](src/Groundtrack/Icons/obstacle.png) | **Obstacle** | POP Collision: footprints (closed curves, up to a height) or massing (breps / meshes); particles go around or bounce off. |
| ![](src/Groundtrack/Icons/crossings.png) | **Crossings** | Where two sets of curves cross (e.g. people vs vehicles, or two sites laid over each other), at what angle, plus a heat map. |
| ![](src/Groundtrack/Icons/timeline.png) | **Timeline** | One clock (seconds) for Particles, Replay and time-sliced Fields. Double-click it to open the timeline window: play / pause, scrub, speed, loop, in / out, and **Record**, which saves the Rhino viewport as a PNG sequence. |
| ![](src/Groundtrack/Icons/replay.png) | **Replay** | The real tracks at time T: everyone's position and trail, coloured by speed. It follows its field when you move or tilt it. |
| ![](src/Groundtrack/Icons/gradient.png) | **Gradient** | Recolour: presets (groundtrack, inferno, viridis, magma, greys, white-pink, blue-red, mono) or your own colours. Plug into Streamlines / Particles / Replay / Crossings. |

Every force has a **Layers** input that makes it act on chosen layers only, so you can mix and
match. For example: a Field Force from layer B that pulls only layer A.

The colours are the groundtrack ramp from blue (slow) to red (fast). Each group has its own scale:
people 0–2.5 m/s, vehicles 0–15 or 0–35 m/s. Right-click a component and choose **Bake** to bake
the lines and points with their colours.

## Install (Rhino 8, Windows)

1. Close Rhino.
2. From the [latest release](https://github.com/LCS3002/groundtrack-grasshopper/releases), either:
   - **zip:** copy `net7.0-windows/groundtrack.gha` to `%APPDATA%\Grasshopper\Libraries\groundtrack\`.
     Use `net48/groundtrack.gha` instead if you start Rhino in .NET Framework mode (`/netfx`).
     If Windows blocked the download: right-click the file → Properties → Unblock.
   - **Yak package:**
     `"C:\Program Files\Rhino 8\System\Yak.exe" install groundtrack-0.3.0-rh8_0-win.yak`.
     This picks the right build for your Rhino by itself.

   Use one of the two, not both: two copies make Grasshopper report a conflict.
3. Start Rhino and Grasshopper. The **groundtrack** tab appears.

When you open a definition before the plugin is installed, Rhino offers to fetch the plugin from
its package server. That fails, because groundtrack isn't published there: install it as above.

## Quick start: two nodes

1. Place **Field**. The Site / Clip / Group dropdowns appear next to it on their own. The results
   folder is found automatically: a folder called `GROUNDTRACK RESULTS` under Documents, or
   whatever the `GROUNDTRACK_RESULTS` environment variable points to. Otherwise connect your folder
   to **Folder**; it is remembered.
2. Connect **Streamlines**. That's it.

Then try:
- **Field → Particles ← Timeline.** Double-click the Timeline, press Play, scrub, or Record.
- **Field → Replay ← Timeline.** The real people at that moment.
- **Two Fields** (e.g. two clips, or two sites moved next to / over each other) → **Blend Fields**
  with T.
- **Streamlines of two fields → Crossings.** Where the flows meet, and the heat map.
- **Field Time ← Timeline.** The field of just that moment (e.g. ±2.5 s); Particles keep flowing
  as it changes.
- **Field → Rotate 3D (about X) → Streamlines / Particles.** The field tilts or stands up, and the
  flow runs along it.
- **Two levels joined by ramps:** a second field moved up and beside the first (Transform Field),
  3D ramp curves between them → Field from Curves, all three → Merge Fields. One continuous flow
  that climbs the ramps.
- **A folded surface:** copies of a field hinged edge to edge with Move + Rotate 3D (floor, slope,
  wall), all into Particles. With Join, particles run from one onto the next.

`examples/groundtrack_example.gh` shows all of this, in seven groups side by side:
1. Streamlines.
2. Particles and Replay on a Timeline.
3. Blend and Crossings.
4. Two layers stacked 8 m apart that intertwine (Interact) and swirl (Axis Force).
5. The plaza stood up on its south edge with Rotate 3D.
6. Two levels joined by ramps: the plaza, and plaza 3 30 m north and 5 m up, with two drawn ramps,
   merged into one continuous field.
7. A folded surface: a motorway clip three times, hinged as floor, 30° slope and wall; the vehicles
   drive from one onto the next.

Groups 1–6 use Reuters Plaza; group 7 uses the Blackwall motorway (its vehicles drive straight
through, which shows the hand-over clearly).

## Data and coordinates

The Field component reads the folder structure that groundtrack's results have:

```
<site>/composite/houdini/vector_field_*.csv   the whole site (all clips)
<site>/composite/houdini/points.csv           its tracks (for heights, Replay, time-slices)
<site>/clips/<clip>/houdini/...               the same per clip
```

Coordinates:
- Units are metres around the site's origin, which is written in its groundtrack scripts, so all
  clips of a site and the whole line up. Easting → +X, Northing → +Y.
- Z is the ground height above the site's ground level. It comes from the track heights, so on
  Trafalgar Square the terrace sits 3 m above the square.
- Fields of different sites all sit around their own origin. Laid over each other they overlap,
  which is handy for collages; use Move to place them apart.

No data is bundled with the plugin; it reads your local results only.

## Tested

Run in real Rhino 8.29 with Grasshopper, headless (Rhino.Inside), on the plaza, motorway and
Trafalgar Square fields. `tests/rhino/run_tests.py` prints the numbers; the run that produced these
passed all of them, on both .NET 8 and .NET Framework 4.8:

| Check | Plaza | Motorway | Trafalgar |
|---|---|---|---|
| Streamlines vs the field direction (95th percentile) | 0.41° | 0.01° | 1.63° |
| Particle speed vs field speed under them (median deviation) | 0.11 % | 0.09 % | 0.08 % |
| Particles outside the field | 0 | 0 | 0 |
| Time per step, ~3000 particles (.NET 8; laptop on mains / on battery) | 0.8 / 1.3 ms | 0.8 / 1.5 ms | 0.9 / 1.4 ms |

- **Blend:** at T = 0 and T = 1 it returns its inputs exactly (largest difference 0, over 10,595
  and 5,433 cells).
- **Crossings:** a known grid of 5 × 7 lines gives exactly 35 crossings at 90°. People × vehicles
  on Trafalgar Square give 393 crossings.
- **Replay:** at 5 s, 12.5 s and 21.3 s, everyone sits exactly at the data's positions (0.00 cm
  off). Scrubbing back gives identical positions.
- **Time-sliced field:** 182 cells, the same as counted directly from the data.
- **Recording:** in visible Rhino, the timeline window opens, and recording 0–1 s at 30 fps writes
  31 PNG frames.

Forces, layers, seams, tilting, ramps and folds (0.2–0.3), on the same data:

| Check | Result |
|---|---|
| Two plaza layers 8 m apart, particles off their own layer | 0 % without Interact, 41 % with |
| Axis Force: mean angular momentum about the axis | −1.2 → 12.5 m²/s |
| Attract: mean distance to the point | 16.5 → 14.0 m |
| Obstacle: particles inside a block at 3 / 6 / 9 / 12 s | 0 / 0 / 0 / 0 |
| Field Force: plaza 1 particles vs plaza 3's flow (median angle) | 89° → 11° |
| Field from Curves: streamlines vs the curve (95th percentile) | 0.3° |
| Merge of two Trafalgars 26 m apart: streamline segments steeper than 2:1 | 1,428 hard → 0 with the defaults |
| Tilting Trafalgar with Grasshopper's Rotate (YZ plane), Rotate 3D, Rotate Axis (stood up) and Orient: cells and velocities vs the node's transform | largest error 2e-14 m |
| … streamlines along the tilted flow (95th percentile) / particles outside / particle speed vs the field | 1.6° / 0 / 0.07 % |
| Replay on a plaza rotated 50° about X vs the rotated real positions | 0 m |
| Transform Field 30°: velocity error | 6e-14 m/s |
| ~3000 particles with Interact + Axis Force + Obstacle | 8 ms per step (13 ms on battery) |
| Two plaza levels 5 m apart joined by two ramps: streamline segments steeper than 2:1 / people who walk between the levels in 90 s | 0 / 431 up, 365 down |
| Folded motorway (floor, 30° slope, wall; Join 8 m): vehicles in 90 s from the floor onto the slope / on up the wall | 11,730 / 2,765 (none with Join 0) |
| Definitions saved with 0.1.1 and 0.2.0 | open without messages, wires kept |

## Build

```
dotnet build src/Groundtrack -c Release          # bin/Release/net7.0-windows and net48
dotnet test tests/unit                            # the maths, without Rhino
python tools/install.py                           # copy the build into Grasshopper's Libraries
python tests/rhino/run_tests.py "<GROUNDTRACK RESULTS>" --fw net8.0 --example examples/groundtrack_example.gh
python tools/package.py                           # dist/: zip + Yak package
```

It needs the .NET SDK (8 or later) and Rhino 8; the tests also need `pip install rhinoinside`.

## Licence

MIT
