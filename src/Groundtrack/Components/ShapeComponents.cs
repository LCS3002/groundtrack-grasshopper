using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Groundtrack.Core;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Groundtrack.Components
{
    /// <summary>Quick bends of a field into 3D: twist, bend, swirl, wave, taper, bulge.</summary>
    public class WarpFieldComponent : GH_Component
    {
        public WarpFieldComponent()
            : base("Warp Field", "Warp",
                   "Bend a field into 3D: twist (about the axis), bend (roll up along it), swirl (vortex in plan), wave (undulate), " +
                   "taper (narrow along the axis) or bulge (a hill / crater). The flow runs along the new shape: particles and " +
                   "streamlines follow. Grasshopper's own Bend / Twist / Taper / Box Morph / Surface Morph work on fields too.",
                   Tab.Name, Tab.Shape) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e520");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("warpfield");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field", GH_ParamAccess.item);
            p.AddTextParameter("Mode", "M", "twist / bend / swirl / wave / taper / bulge", GH_ParamAccess.item, "wave");
            p.AddNumberParameter("Amount", "A", "Degrees (twist, bend, swirl), metres (wave height, bulge height; negative: down) or the far end's scale (taper). Empty: a good start for this field", GH_ParamAccess.item);
            p.AddNumberParameter("Size", "S", "Wavelength (wave) or radius (swirl, bulge), m. Empty: from the field's size", GH_ParamAccess.item);
            p.AddLineParameter("Axis", "X", "Along this line (twist, bend, wave, taper) / about its middle (swirl, bulge). Empty: through the field's centre along its longer side", GH_ParamAccess.item);
            p.AddVectorParameter("Up", "U", "The field's up for bend / wave / swirl / bulge; empty: the field's own", GH_ParamAccess.item);
            p[2].Optional = true; p[3].Optional = true; p[4].Optional = true; p[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
            => p.AddParameter(new FieldParam(), "Field", "F", "The warped field", GH_ParamAccess.item);

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            string mode = "wave";
            double amount = double.NaN, size = double.NaN;
            var axis = Line.Unset;
            var upv = Vector3d.Unset;
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            DA.GetData(1, ref mode); DA.GetData(2, ref amount); DA.GetData(3, ref size);
            bool hasAxis = DA.GetData(4, ref axis) && axis.IsValid && axis.Length > 1e-9;
            bool hasUp = DA.GetData(5, ref upv) && upv.IsValid && upv.Length > 1e-9;
            var f = g.Value;
            var m = ParseMode(mode);
            var (x0, y0, z0, x1, y1, z1) = f.WorldBox();
            double ex = x1 - x0, ey = y1 - y0, big = Math.Max(ex, ey);
            var centre = new Point3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
            var dir = ex >= ey ? Vector3d.XAxis : Vector3d.YAxis;
            double len = ex >= ey ? ex : ey;
            var start = centre - dir * len / 2;
            if (hasAxis)
            {
                dir = axis.Direction; dir.Unitize();
                len = axis.Length; start = axis.From;
                centre = axis.PointAt(0.5);
            }
            var up = hasUp ? upv : NormalNear(f, centre);
            up.Unitize();
            if (double.IsNaN(amount))
                amount = m == Shape.WarpMode.Twist ? 90 : m == Shape.WarpMode.Bend ? 90 : m == Shape.WarpMode.Swirl ? 120
                       : m == Shape.WarpMode.Wave ? 0.08 * len : m == Shape.WarpMode.Taper ? 0.4 : 0.15 * big;
            if (double.IsNaN(size))
                size = m == Shape.WarpMode.Wave ? len / 2 : m == Shape.WarpMode.Swirl ? 0.5 * big : 0.4 * big;
            var o = m == Shape.WarpMode.Swirl || m == Shape.WarpMode.Bulge ? centre : start;
            var map = Shape.Warp(m, new[] { o.X, o.Y, o.Z }, new[] { dir.X, dir.Y, dir.Z }, len, new[] { up.X, up.Y, up.Z }, amount, size);
            DA.SetData(0, new FieldGoo(f.Morphed(map)));
            Message = $"{m.ToString().ToLowerInvariant()} {amount:0.#}";
        }

        internal static Shape.WarpMode ParseMode(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.StartsWith("t") && s.Contains("w")) return Shape.WarpMode.Twist;
            if (s.StartsWith("ta")) return Shape.WarpMode.Taper;
            if (s.StartsWith("be")) return Shape.WarpMode.Bend;
            if (s.StartsWith("s")) return Shape.WarpMode.Swirl;
            if (s.StartsWith("bu")) return Shape.WarpMode.Bulge;
            return Shape.WarpMode.Wave;
        }

        internal static Vector3d NormalNear(FieldData f, Point3d p)
        {
            if (f.SampleWorld(p.X, p.Y, p.Z, out var s)) return new Vector3d(s.Nx, s.Ny, s.Nz);
            var (nx, ny, nz) = f.Place.Normal;
            return new Vector3d(nx, ny, nz);
        }
    }

    /// <summary>A field's ground as a landscape: softened, raised where it is busy, or draped
    /// over a surface you model.</summary>
    public class LandscapeComponent : GH_Component
    {
        public LandscapeComponent()
            : base("Landscape", "Land",
                   "Shape a field's ground: Soften (m) rolls steps and cliffs (e.g. where merged layers meet) into hills; Relief " +
                   "raises it where it is busiest or fastest; Ground drapes it over a surface / mesh you model (Offset above it). " +
                   "The flow rides over the new ground.",
                   Tab.Name, Tab.Shape) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e521");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("landscape");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field", GH_ParamAccess.item);
            p.AddNumberParameter("Soften", "S", "Roll the ground over this many metres (0 = off)", GH_ParamAccess.item, 0.0);
            p.AddTextParameter("Relief by", "By", "Raise where it is: density (busiest) / speed (fastest) / none", GH_ParamAccess.item, "density");
            p.AddNumberParameter("Relief", "R", "How much higher the busiest / fastest ground gets (m; negative: lower)", GH_ParamAccess.item, 0.0);
            p.AddGeometryParameter("Ground", "G", "Drape onto this surface / Brep / mesh (straight down, or across a tilted field)", GH_ParamAccess.list);
            p.AddNumberParameter("Offset", "O", "Height above the Ground (m)", GH_ParamAccess.item, 0.0);
            p[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
            => p.AddParameter(new FieldParam(), "Field", "F", "The reshaped field", GH_ParamAccess.item);

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            double soften = 0, relief = 0, offset = 0;
            string by = "density";
            var ground = new List<IGH_GeometricGoo>();
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            DA.GetData(1, ref soften); DA.GetData(2, ref by); DA.GetData(3, ref relief);
            DA.GetDataList(4, ground); DA.GetData(5, ref offset);
            var f = g.Value;
            var mesh = GroundMesh(ground);
            int draped = 0;
            if (mesh != null)
            {
                var dz = new double[f.Count];
                for (int k = 0; k < f.Count; k++)
                {
                    if (!f.Has[k]) continue;
                    var (x, y, z) = f.CellWorld(k);
                    var (nx, ny, nz) = f.CellNormal(k);
                    var p = new Point3d(x, y, z);
                    var n = new Vector3d(nx, ny, nz);
                    double t = Intersection.MeshRay(mesh, new Ray3d(p + n * 1e5, -n));
                    if (t >= 0) { dz[k] = 1e5 - t + offset; draped++; }
                }
                f = Shape.Lift(f, dz);
            }
            if (soften > 0 || relief != 0) f = Shape.Lift(f, Shape.Landscape(f, Math.Max(0, soften), by, relief));
            DA.SetData(0, new FieldGoo(f));
            Message = mesh != null ? $"draped {draped} cells" : "";
        }

        static Mesh GroundMesh(List<IGH_GeometricGoo> goos)
        {
            var m = new Mesh();
            foreach (var goo in goos)
            {
                if (goo == null) continue;
                if (goo.CastTo(out Mesh mesh) && mesh != null) { m.Append(mesh); continue; }
                Brep brep = null;
                if (goo.CastTo(out Brep b)) brep = b;
                else if (goo.CastTo(out Surface s) && s != null) brep = s.ToBrep();
                if (brep == null) continue;
                foreach (var part in Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh) ?? Array.Empty<Mesh>()) m.Append(part);
            }
            return m.Faces.Count > 0 ? m : null;
        }
    }

    /// <summary>Stacked layers joined at their edges by rounded folds: one continuous landscape.</summary>
    public class LoftFieldsComponent : GH_Component
    {
        public LoftFieldsComponent()
            : base("Loft Fields", "Loft",
                   "Join stacked layers at their edges: from each layer's outline a rounded fold swings out and lands on the next " +
                   "layer's outline, and the flow blends from one into the other across it. Fields in stacking order (top to " +
                   "bottom or the other way). Use All in Particles (Join) / Streamlines: one continuous landscape.",
                   Tab.Name, Tab.Shape) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e522");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("loft");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Fields", "F", "Two or more stacked fields, in order", GH_ParamAccess.list);
            p.AddNumberParameter("Start", "S", "Where along the outline the folds start (0..1)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("End", "E", "Where they end (0..1); 0 to 1 = all the way round", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Swing", "Sw", "How far the folds swing out (x the gap between the layers)", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Folds", "Fo", "The folds between consecutive layers (fields)", GH_ParamAccess.list);
            p.AddParameter(new FieldParam(), "All", "A", "Layers and folds in order: plug into Particles (Join) or Streamlines", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var goos = new List<FieldGoo>();
            double s = 0, e = 1, swing = 1;
            if (!DA.GetDataList(0, goos)) return;
            DA.GetData(1, ref s); DA.GetData(2, ref e); DA.GetData(3, ref swing);
            var fields = goos.Where(q => q?.Value != null).Select(q => q.Value).ToList();
            if (fields.Count < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Two or more fields"); return; }
            var folds = new List<FieldGoo>();
            var all = new List<FieldGoo> { new FieldGoo(fields[0]) };
            for (int i = 0; i + 1 < fields.Count; i++)
            {
                var fold = Shape.Loft(fields[i], fields[i + 1], s, e, Math.Max(0.05, swing));
                if (fold != null) { folds.Add(new FieldGoo(fold)); all.Add(new FieldGoo(fold)); }
                else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"No outline for field {i} or {i + 1}");
                all.Add(new FieldGoo(fields[i + 1]));
            }
            DA.SetDataList(0, folds);
            DA.SetDataList(1, all);
            Message = $"{folds.Count} folds";
        }
    }

    /// <summary>Ribbons between fields: found where one field's flow leaves and another's comes in,
    /// or along curves you draw.</summary>
    public class BridgeComponent : GH_Component
    {
        public BridgeComponent()
            : base("Bridge", "Bridge",
                   "Swooping ribbons between fields: automatically where one field's flow leaves its edge and another's comes in " +
                   "(Count per pair and direction, within Reach), and along any Curves you draw. Each is a field: plug All into " +
                   "Particles (Join) / Streamlines.",
                   Tab.Name, Tab.Shape) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e523");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("bridge");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Fields", "F", "Fields to bridge between", GH_ParamAccess.list);
            p.AddCurveParameter("Curves", "C", "Bridges of your own (their direction is the flow's)", GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Automatic bridges per pair of fields and direction (0 = none)", GH_ParamAccess.item, 1);
            p.AddNumberParameter("Width", "W", "Ribbon width (m)", GH_ParamAccess.item, 4.0);
            p.AddNumberParameter("Swing", "Sw", "How far the bridges swing out along the flow (x their length)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Reach", "R", "Longest automatic bridge (m)", GH_ParamAccess.item, 80.0);
            p.AddNumberParameter("Speed", "Sp", "Speed along drawn bridges (m/s)", GH_ParamAccess.item, 1.3);
            p[0].Optional = true; p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Bridges", "B", "The ribbons (fields)", GH_ParamAccess.list);
            p.AddCurveParameter("Curves", "C", "Their centre lines", GH_ParamAccess.list);
            p.AddParameter(new FieldParam(), "All", "A", "The fields and the bridges: plug into Particles (Join) or Streamlines", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var goos = new List<FieldGoo>();
            var curves = new List<Curve>();
            int count = 1;
            double width = 4, swing = 1, reach = 80, speed = 1.3;
            DA.GetDataList(0, goos); DA.GetDataList(1, curves);
            DA.GetData(2, ref count); DA.GetData(3, ref width); DA.GetData(4, ref swing); DA.GetData(5, ref reach); DA.GetData(6, ref speed);
            var fields = goos.Where(q => q?.Value != null).Select(q => q.Value).ToList();
            width = Math.Max(0.5, width);
            var bridges = new List<FieldGoo>();
            var lines = new List<Curve>();
            for (int a = 0; a < fields.Count && count > 0; a++)
                for (int b = 0; b < fields.Count; b++)
                {
                    if (a == b) continue;
                    foreach (var (curve, sp, ua, ub) in Shape.AutoBridges(fields[a], fields[b], count, width, reach, Math.Max(0.05, swing)))
                    {
                        var r = Shape.Ribbon(curve, width, Math.Max(sp, 0.1), t => Mix(ua, ub, t), group: fields[a].Group);
                        if (r == null) continue;
                        bridges.Add(new FieldGoo(r));
                        lines.Add(new PolylineCurve(curve.Select(q => new Point3d(q.X, q.Y, q.Z))));
                    }
                }
            foreach (var c in curves)
            {
                var pts = Geo.Poly(c, Math.Max(width / 4, 0.25));
                var r = Shape.Ribbon(pts, width, Math.Max(speed, 0.05), t => new[] { 0.0, 0, 1 });
                if (r == null) continue;
                bridges.Add(new FieldGoo(r));
                lines.Add(c.DuplicateCurve());
            }
            DA.SetDataList(0, bridges);
            DA.SetDataList(1, lines);
            DA.SetDataList(2, fields.Select(q => new FieldGoo(q)).Concat(bridges));
            Message = $"{bridges.Count} bridges";
        }

        static double[] Mix(double[] a, double[] b, double t)
        {
            var v = new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t };
            double l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return l < 1e-9 ? new[] { 0.0, 0, 1 } : new[] { v[0] / l, v[1] / l, v[2] / l };
        }
    }

    /// <summary>Forces baked into a field: the flow itself bends (swirls, pulls, avoids).</summary>
    public class ApplyForcesComponent : GH_Component
    {
        public ApplyForcesComponent()
            : base("Apply Forces", "Bend",
                   "Bend a field's flow with forces (Force, Axis Force, Attract, Curve Force, Field Force, Obstacle): at every " +
                   "cell the push is added for Seconds and kept along the ground. Keep speed: only the direction changes. " +
                   "Streamlines then show the bent flow; Warp Field bends the shape, this bends the flow.",
                   Tab.Name, Tab.Forces) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e524");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("applyforces");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field", GH_ParamAccess.item);
            p.AddParameter(new ForceParam(), "Forces", "Frc", "Forces", GH_ParamAccess.list);
            p.AddNumberParameter("Seconds", "S", "How long the forces push (s): more = bent further", GH_ParamAccess.item, 1.0);
            p.AddBooleanParameter("Keep speed", "K", "Keep each cell's speed (only the direction changes)", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
            => p.AddParameter(new FieldParam(), "Field", "F", "The field with the bent flow", GH_ParamAccess.item);

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            var forces = new List<ForceGoo>();
            double seconds = 1;
            bool keep = true;
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            DA.GetDataList(1, forces); DA.GetData(2, ref seconds); DA.GetData(3, ref keep);
            var list = forces.Where(q => q?.Value != null).Select(q => q.Value).ToList();
            DA.SetData(0, new FieldGoo(list.Count == 0 ? g.Value : Shape.ApplyForces(g.Value, list, seconds, keep)));
            Message = $"{list.Count} forces";
        }
    }

    /// <summary>Where layers cut each other (like intersecting planes), and how their flows meet there.</summary>
    public class IntersectFieldsComponent : GH_Component
    {
        public IntersectFieldsComponent()
            : base("Intersect Fields", "Cut",
                   "Where fields' grounds cut through each other (tilted, warped or stacked layers): the cut lines, and the angle " +
                   "between the two flows along each.",
                   Tab.Name, Tab.Analyse) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e525");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("intersect");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
            => p.AddParameter(new FieldParam(), "Fields", "F", "Two or more fields", GH_ParamAccess.list);

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Curves", "C", "The cut lines", GH_ParamAccess.list);
            p.AddNumberParameter("Angle", "A", "Median angle between the two flows along each cut (deg)", GH_ParamAccess.list);
            p.AddTextParameter("Pair", "P", "Which fields each cut is between (i x j)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var goos = new List<FieldGoo>();
            if (!DA.GetDataList(0, goos)) return;
            var fields = goos.Where(q => q?.Value != null).Select(q => q.Value).ToList();
            var meshes = fields.Select(f => FieldMesh.Build(f, null)).ToList();
            var curves = new List<Curve>();
            var angles = new List<double>();
            var pairs = new List<string>();
            double tol = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
            for (int a = 0; a < fields.Count; a++)
                for (int b = a + 1; b < fields.Count; b++)
                {
                    if (meshes[a] == null || meshes[b] == null) continue;
#pragma warning disable CS0618
                    var cuts = Intersection.MeshMeshAccurate(meshes[a], meshes[b], tol);
#pragma warning restore CS0618
                    if (cuts == null) continue;
                    foreach (var pl in cuts)
                    {
                        if (pl == null || pl.Count < 2) continue;
                        var angs = new List<double>();
                        foreach (var q in pl)
                        {
                            if (!fields[a].SampleWorld(q.X, q.Y, q.Z, out var sa) || !fields[b].SampleWorld(q.X, q.Y, q.Z, out var sb)) continue;
                            double la = sa.Speed, lb = sb.Speed;
                            if (la < 1e-6 || lb < 1e-6) continue;
                            double c = (sa.Vx * sb.Vx + sa.Vy * sb.Vy + sa.Vz * sb.Vz) / (la * lb);
                            angs.Add(Math.Acos(Math.Max(-1, Math.Min(1, c))) * 180 / Math.PI);
                        }
                        angs.Sort();
                        curves.Add(new PolylineCurve(pl));
                        angles.Add(angs.Count > 0 ? angs[angs.Count / 2] : double.NaN);
                        pairs.Add($"{a} x {b}");
                    }
                }
            DA.SetDataList(0, curves);
            DA.SetDataList(1, angles);
            DA.SetDataList(2, pairs);
            Message = $"{curves.Count} cuts";
        }
    }

    /// <summary>A field's ground as a coloured mesh, and its outlines.</summary>
    public class FieldSurfaceComponent : GtComponent
    {
        public FieldSurfaceComponent()
            : base("Field Surface", "Surf",
                   "A field's ground as a mesh coloured by speed (also bent fields, folds and bridges), and its outlines as smooth " +
                   "curves: bake them, loft them, render them.", Tab.Shape) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e526");
        protected override Bitmap Icon => Icons.Get("surface");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field", GH_ParamAccess.item);
            p.AddParameter(new GradientParam(), "Gradient", "G", "Recolour; empty = the groundtrack ramp", GH_ParamAccess.item);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M", "The ground, coloured by speed", GH_ParamAccess.item);
            p.AddCurveParameter("Outlines", "O", "Its outlines (smooth closed curves; the longest first)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            GradientGoo grad = null;
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            DA.GetData(1, ref grad);
            var f = g.Value;
            var mesh = FieldMesh.Build(f, grad?.Value);
            if (mesh != null) { Meshes.Add(mesh); DA.SetData(0, mesh); }
            var outlines = new List<Curve>();
            foreach (var loop in Shape.Outlines(f))
            {
                var pts = loop.Select(q =>
                {
                    double lz = f.SampleLocal(q.X, q.Y, out var s) ? s.Z : 0;
                    var (x, y, z) = f.LocalToWorld(q.X, q.Y, lz);
                    return new Point3d(x, y, z);
                }).ToList();
                pts.Add(pts[0]);
                var c = new PolylineCurve(pts);
                outlines.Add(c);
                for (int k = 1; k < pts.Count; k++) Buckets.Add(new Line(pts[k - 1], pts[k]), Color.FromArgb(40, 40, 44));
            }
            DA.SetDataList(1, outlines);
            Message = $"{f.Valid} cells";
        }
    }

    /// <summary>Meshes of fields' grounds.</summary>
    internal static class FieldMesh
    {
        /// <summary>A quad mesh through the cell centres with data, coloured by speed.</summary>
        public static Mesh Build(FieldData f, Gradient gradient)
        {
            var m = new Mesh();
            var idx = new int[f.Count];
            for (int k = 0; k < f.Count; k++)
            {
                idx[k] = -1;
                if (!f.Has[k]) continue;
                var (x, y, z) = f.CellWorld(k);
                idx[k] = m.Vertices.Add(x, y, z);
                var (vx, vy, vz) = f.CellVelocity(k);
                double sp = Math.Sqrt(vx * vx + vy * vy + vz * vz) / Math.Max(f.Scale, 1e-9);
                m.VertexColors.Add(Preview.SpeedColour(sp, f.Smax[k] > 0 ? f.Smax[k] : 2.5, gradient));
            }
            for (int j = 0; j + 1 < f.Ny; j++)
                for (int i = 0; i + 1 < f.Nx; i++)
                {
                    int a = idx[j * f.Nx + i], b = idx[j * f.Nx + i + 1], c = idx[(j + 1) * f.Nx + i + 1], d = idx[(j + 1) * f.Nx + i];
                    int n = (a >= 0 ? 1 : 0) + (b >= 0 ? 1 : 0) + (c >= 0 ? 1 : 0) + (d >= 0 ? 1 : 0);
                    if (n == 4) m.Faces.AddFace(a, b, c, d);
                    else if (n == 3)
                    {
                        var t = new[] { a, b, c, d }.Where(v => v >= 0).ToArray();
                        m.Faces.AddFace(t[0], t[1], t[2]);
                    }
                }
            if (m.Faces.Count == 0) return null;
            m.Normals.ComputeNormals();
            m.Compact();
            return m;
        }
    }
}
