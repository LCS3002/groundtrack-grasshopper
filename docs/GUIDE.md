# groundtrack for Grasshopper — quick guide (0.5)

## Install / update
- **Installed:** `%APPDATA%\Grasshopper\Libraries\groundtrack\groundtrack.gha`.
- **To update:** close Rhino, then replace that file.
- **Keep one copy only.** A second copy (for example the .yak package as well) makes Grasshopper report a conflict.
- **"Package not found" popup:** the plugin isn't loaded in that Rhino. Ignore the package server and install as above.
- **Example file:** `GROUNDTRACK RESULTS\06_grasshopper\groundtrack_example.gh`. It has 10 groups side by side: streamlines, particles + replay, blend + crossings, layers + forces, a field stood up, levels joined by ramps, a folded surface, a lofted landscape, bend + bridge + cut, fuse + sculpt + Houdini.

## Data
- **Field** finds `GROUNDTRACK RESULTS` by itself. Its **Site / Clip / Group** dropdowns appear when you place it.
- **Clip `whole`** = all clips of the site combined.
- **Group:** `all`, `people`, `vehicles`, `cycles` or `buses`.
- **Units:** metres. East → +X, North → +Y, Z = ground height (the Trafalgar terrace sits 3 m up).
- **Origins:** every site sits at its own origin, so sites lie on top of each other until you move them.

## Nodes (tab: groundtrack)
| Panel | Node | Does |
|---|---|---|
| Data | **Field** | Loads a field. Give it a Time to get just the movement around that moment. |
| Data | **Transform Field** | Rotate (**degrees**, about its own centre), Scale, Stretch X/Y, Mirror (all in the field's own plane), then Move (Z = stack layers). |
| Data | **Field from Curves** | Draw your own flow: it runs along the curves (Speed, Width). |
| Flow | **Streamlines** | Flow lines. One Density slider. |
| Flow | **Particles** | The POP simulation. Several fields = several layers. Plug forces into Forces. |
| Flow | **Merge Fields** | Several fields → one: sites, clips and groups together. |
| Flow | **Blend Fields** | Morph between fields with the T slider (0 = first field, 1 = last). |
| Flow | **Smooth Field** | Relax a field: Radius, Fill gaps, Max slope. |
| Shape | **Warp Field** | Bend a field into 3D: twist / bend / swirl / wave / taper / bulge (Amount, Size, Axis, Up; empty = a good start). |
| Shape | **Landscape** | Soften (rolls steps into hills), Relief (higher where busy / fast), Ground (drape onto your surface / mesh, + Offset). |
| Shape | **Loft Fields** | Stacked layers joined at their edges by rounded folds; Start / End = which part of the outline; Swing = how far the folds bulge. |
| Shape | **Bridge** | Ribbons between fields: automatic (Count per pair, Reach) or along your Curves; Width, Swing. |
| Shape | **Sculpt Field** | Terrain: press Areas (closed curves), valleys / ridges (open curves, Width) or Points (Radius) down (Height < 0) or up; Falloff = soft edge; Downhill = flow runs down the slopes. |
| Shape | **Fuse Fields** | Blob join: fields fuse where their edges come within Reach — only the near sides. |
| Shape | **Connect Fields** | The same join, only where you draw (Where: areas / curves). Looks like the fields themselves. |
| Shape | **Field Surface** | The ground as a coloured mesh + smooth outline curves. |
| Forces | **Force** | Constant push plus turbulence (Noise, Scale, Speed). |
| Forces | **Axis Force** | Swirl around an axis (Orbit), suck in (Suction), lift (Lift). |
| Forces | **Attract** | Points pull in; negative Strength pushes away. |
| Forces | **Curve Force** | Particles follow, are attracted to, or repelled by curves. |
| Forces | **Field Force** | A field's vectors as a force on any layer; Height pull drags particles to its level. |
| Forces | **Interact** | Separation, cohesion and alignment between particles. `others` = layers pull on each other. |
| Forces | **Obstacle** | Footprints (closed curves) or massing (breps/meshes): particles go around. |
| Forces | **Apply Forces** | Bends the field's flow itself with forces (Seconds = how far; Keep speed). |
| Analyse | **Crossings** | Where two sets of curves cross, at what angle, plus a heat map. |
| Analyse | **Intersect Fields** | Where layers cut through each other: cut lines + flow angle. |
| Time | **Timeline** | The clock. **Double-click** for the window: play, scrub, speed, loop, in/out, **Record** (PNGs). |
| Time | **Replay** | The real people/vehicles at time T, with trails. |
| Export | **Houdini Export** | Fields → Houdini POP kit: one velocity volume as a force field (Mode force / velocity, Air resistance), .vdb + .geo. Export = true, then paste the Houdini line into Houdini's Python Shell. |
| Colour | **Gradient** | Recolour with presets or your own colours. Plug into the Gradient input of Streamlines/Particles/Replay/Crossings. |

## Houdini POP → here
| Houdini | groundtrack |
|---|---|
| POP Source | Particles (born where people were) — Rate, Life |
| POP Advect by Volumes, velocity | Particles **Response 0** (follows exactly) |
| POP Advect, force / POP Wind | Particles **Response 0.3–1** (inertia), **Field Force** |
| POP Force | **Force** |
| POP Axis Force | **Axis Force** |
| POP Attract | **Attract**, **Curve Force** |
| POP Interact / flocking | **Interact** |
| POP Collision | **Obstacle** |
| POP Drag | Particles **Drag** |
| POP Kill (outside) | default. Particles **Outside = true** keeps them alive outside their field. |
| Playbar | **Timeline** (double-click) |

## Recipes
1. **Look:** Field → Streamlines.
2. **Animate:** Field → Particles ← Timeline. Double-click the Timeline → Play / scrub / Record.
3. **Real people:** Field → Replay ← Timeline.
4. **Place, collage, tilt:**
   - Field → **Transform Field**, or Grasshopper's Move / Rotate.
   - **Tilt / stand up:** Grasshopper's **Rotate 3D**, **Rotate Axis** or **Orient** (or Rotate with a YZ plane). The field tilts, and its flow, streamlines, particles and replay go with it. Hold and Field Force then work across the tilted ground.
5. **Stack on Z and intertwine:**
   - Two Fields → Transform Field (Move Z, e.g. 8 m) → both into Particles **Fields**.
   - On Particles: **Hold 0.2** (free to leave the ground), **Colour = layer**.
   - Add **Interact** with Between `others`, Radius ~10, Cohesion 1–2.
6. **Mix & match forces:** plug any number into **Forces**. Each force has a **Layers** input: `0` = only the first field, `1` = only the second. For example, a Field Force from layer B acting only on layer A.
7. **Vectors as a force:**
   - **Field Force** — Strength = how hard it pulls, Height pull = drag to that field's height.
   - Particles **Response > 0** for inertia.
8. **One field from many:** **Merge Fields**. The defaults give no seams:
   - **Feather 4 m:** each field fades in at its edges.
   - **Smooth 1.5 m:** rounds the result.
   - **Max slope 1:** no cliffs between layers.
9. **Interpolate:** **Blend Fields** with **T**. To animate it, T = Timeline T ÷ end time (a Division node).
10. **Smooth anything:** **Smooth Field**.
11. **Your own flows:** curves → **Field from Curves** → Merge with the measured fields.
12. **Distort:**
    - Transform Field: Stretch X/Y, Mirror, Scale.
    - Or the results: Points / Trails → Bend, Twist, Box Morph.
13. **Where flows meet:** Streamlines or Trails of A and of B → **Crossings**.
14. **Recolour:** **Gradient** → the Gradient input. Particles **Colour**: `speed` / `layer` / `age`.
15. **Export:** right-click a component → **Bake**. The speed colours are kept.
16. **Levels joined by ramps (one continuous flow):**
    - Lower Field, and an upper Field → Transform Field (Move up, e.g. 5 m, and to the side so they don't overlap; leave a gap of e.g. 30 m).
    - Draw 3D ramp curves across the gap: start a few metres inside the lower field, end a few metres inside the upper one, flat at both ends. Draw them in the direction of the flow (a second curve for the way back).
    - Ramps → **Field from Curves**; lower + upper + ramps → **Merge Fields** (defaults) → Streamlines / Particles.
    - Put the ramps where people actually leave and enter the fields, so the flow carries on into them.
17. **Folded surface (floor → slope → wall):**
    - Copies of a field end to end (Move), each turned about the edge it shares with the one before (**Rotate 3D**: Center on that edge, Axis along it).
    - All copies into Particles **Fields**; set **Join** to the gaps at the edges (e.g. 8 m).
    - Ragged or holey fields: **Smooth Field** (Radius 4, Fill) first.
    - Lay the copies along the flow's start-to-end direction, so the flow leaving one arrives where the next one's flow starts.
18. **3D landscape (stack, bend, join):**
    - Stack: Fields → Transform Field (Move Z 6–10 m each).
    - Bend some: **Warp Field** (wave / bulge / twist / bend), or Grasshopper's Bend Deform, Twist, Taper, Maelstrom, Box Morph, Surface Morph.
    - Shape the ground: **Landscape** (Soften 6–15 m, Relief 5–10 m, or Ground = your own surface).
    - Join them: **Loft Fields** (rounded folds at the edges) and/or **Bridge** (ribbons).
    - Loft's / Bridge's **All** → Particles (Join 2) / Streamlines / Field Surface. Bake → render.
19. **Bend the flow, not the shape:** forces → **Apply Forces** → Streamlines (swirls, detours round massing).
20. **Where layers cut:** tilted or warped layers → **Intersect Fields** → cut lines (plane intersections), flow angle along them.
21. **Blob joins:** stacked or neighbouring fields → **Fuse Fields** (Reach = how close before they join) → All → Particles (Join 2) / Streamlines.
22. **Joins where you want them:** draw areas or curves → **Connect Fields** (Where) → All.
23. **Terrain:** **Sculpt Field**: Points (hollows / hills), open curves (valleys / ridges), closed curves (areas); Downhill 0.5–1 so the flow runs into the hollows.
24. **To Houdini (force field for POPs):** All → **Houdini Export** → Export = true → paste the Houdini line into Houdini's Python Shell (Windows > Python Shell). In your own POP network: POP Advect by Volumes → SOP path /obj/combined/OUT_FIELD (or the .vdb), field **vel**, Update Force + Air Resistance.
25. **Thicker export images:** Width input (2–4) on the drawing nodes; Timeline window **Size** 2–4x for Record.

## Particles settings
| Input | Meaning | Try |
|---|---|---|
| Rate | births per second, per layer | 100–500 |
| Life | seconds (empty = long enough to cross the field) | 5–30 |
| Speed | × measured speed | 0.5–3 |
| Noise | wandering, as a share of the local speed | 0–0.5 |
| Trail | trail length (s) | 1–5 |
| Response | 0 = follow exactly; > 0 = inertia (s) | 0 / 0.5 |
| Hold | stick to the layer's ground: 1 = on it, 0 = free | 1 / 0.2 |
| Drag | how fast pushes from forces fade (1/s) | 0.5–2 |
| Outside | keep particles that leave their field | off |
| Join | fields meeting edge to edge are one surface: particles carry on onto the next one, across gaps up to this many metres (0 = off) | 2 / 8 |
| Colour | speed / layer / age | speed |

## Gotchas
- **Grasshopper Rotate** takes **radians** (360 = 57 turns + 106°): right-click Angle → Degrees. Transform Field takes **degrees**.
- **Merge/Blend average heights where fields overlap.** To keep stacked layers separate, use one Particles node with several Fields instead.
- **Merge/Blend with tilted fields:** the result lies in the plane of the strongest field; the others are projected onto it.
- **Scrubbing backwards** re-runs the simulation from 0. That's expected: it gives the same result every time.
- **Slow?** Lower Rate, or Interact's Radius.
- **Bent fields and Merge / Blend:** these combine fields as they were before bending: merge first, then bend.
- **Join and stacked layers:** Join only hands particles to a field whose ground is within Join of them, and whose flow continues their way. Layers stacked further apart than Join stay separate.
