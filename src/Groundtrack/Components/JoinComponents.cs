using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack.Components
{
    /// <summary>Fields fused where they come close (blob-like): only on the sides that face each
    /// other, within Reach.</summary>
    public class FuseFieldsComponent : GH_Component
    {
        public FuseFieldsComponent()
            : base("Fuse Fields", "Fuse",
                   "Fuse fields where their edges come close, like blobs: wherever two fields' outlines are within Reach of each " +
                   "other, a fold grows from one edge over to the other - fuller the closer they are, fading out at its ends - and " +
                   "the flow blends across it. Only the sides that are near each other join (stacked or side by side). Plug All " +
                   "into Particles (Join) / Streamlines / Field Surface.",
                   Tab.Name, Tab.Shape) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e527");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("fuse");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Fields", "F", "Two or more fields", GH_ParamAccess.list);
            p.AddNumberParameter("Reach", "R", "How close edges must come to fuse (m): more = they join along more of their edges", GH_ParamAccess.item, 12.0);
            p.AddNumberParameter("Swing", "Sw", "How far the folds bulge out (x the gap)", GH_ParamAccess.item, 0.6);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Fusions", "Fu", "The fused stretches (fields)", GH_ParamAccess.list);
            p.AddParameter(new FieldParam(), "All", "A", "The fields and the fusions: plug into Particles (Join) or Streamlines", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var goos = new List<FieldGoo>();
            double reach = 12, swing = 0.6;
            if (!DA.GetDataList(0, goos)) return;
            DA.GetData(1, ref reach); DA.GetData(2, ref swing);
            var fields = goos.Where(q => q?.Value != null).Select(q => q.Value).ToList();
            var fused = JoinTools.Pairs(fields, (a, b) => Shape.Fuse(a, b, Math.Max(0.1, reach), Math.Max(0.05, swing)));
            DA.SetDataList(0, fused.Select(f => new FieldGoo(f)));
            DA.SetDataList(1, fields.Concat(fused).Select(f => new FieldGoo(f)));
            Message = $"{fused.Count} fusions";
        }
    }

    /// <summary>Fields connected smoothly where you say: stacked or side by side.</summary>
    public class ConnectFieldsComponent : GH_Component
    {
        public ConnectFieldsComponent()
            : base("Connect Fields", "Connect",
                   "Connect fields smoothly where you define: closed curves mark the areas, open curves the edges (within Width) " +
                   "where stacked or neighbouring fields join. The join continues the fields themselves - their flow blends " +
                   "across, the same look - not a separate ribbon. Plug All into Particles (Join) / Streamlines / Field Surface.",
                   Tab.Name, Tab.Shape) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e528");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("connect");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Fields", "F", "Two or more fields", GH_ParamAccess.list);
            p.AddCurveParameter("Where", "W", "Closed curves: join inside these areas (seen from above); open curves: join along them", GH_ParamAccess.list);
            p.AddNumberParameter("Width", "Wd", "How far from open curves the join reaches (m)", GH_ParamAccess.item, 10.0);
            p.AddNumberParameter("Reach", "R", "Longest join between the fields' edges (m)", GH_ParamAccess.item, 60.0);
            p.AddNumberParameter("Swing", "Sw", "How far the joins bulge out (x the gap)", GH_ParamAccess.item, 0.8);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Joins", "J", "The joins (fields)", GH_ParamAccess.list);
            p.AddParameter(new FieldParam(), "All", "A", "The fields and the joins: plug into Particles (Join) or Streamlines", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var goos = new List<FieldGoo>();
            var where = new List<Curve>();
            double width = 10, reach = 60, swing = 0.8;
            if (!DA.GetDataList(0, goos)) return;
            DA.GetDataList(1, where); DA.GetData(2, ref width); DA.GetData(3, ref reach); DA.GetData(4, ref swing);
            var fields = goos.Where(q => q?.Value != null).Select(q => q.Value).ToList();
            if (where.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Draw where to connect (Where)"); return; }
            var mask = JoinTools.Mask(where, Math.Max(0.1, width));
            var joins = JoinTools.Pairs(fields, (a, b) => Shape.Fuse(a, b, Math.Max(0.1, reach), Math.Max(0.05, swing), mask));
            DA.SetDataList(0, joins.Select(f => new FieldGoo(f)));
            DA.SetDataList(1, fields.Concat(joins).Select(f => new FieldGoo(f)));
            Message = $"{joins.Count} joins";
        }
    }

    /// <summary>Terrain shaping: press areas of a field down (or raise them), the flow running
    /// downhill if wanted.</summary>
    public class SculptFieldComponent : GH_Component
    {
        public SculptFieldComponent()
            : base("Sculpt Field", "Sculpt",
                   "Landscape a field's ground in areas you define: closed curves (areas), open curves (valleys / ridges, Width " +
                   "wide) and points (Radius) push it down by Height (negative) or up, fading over Falloff. Downhill (0..1) turns " +
                   "the flow down the new slopes, into the hollows and off the hills.",
                   Tab.Name, Tab.Shape) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e529");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("sculpt");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field", GH_ParamAccess.item);
            p.AddCurveParameter("Areas", "A", "Closed curves: areas; open curves: valleys / ridges along them (seen from above)", GH_ParamAccess.list);
            p.AddPointParameter("Points", "P", "Hollows / hills at these points", GH_ParamAccess.list);
            p.AddNumberParameter("Height", "H", "How far down (negative) or up (m)", GH_ParamAccess.item, -4.0);
            p.AddNumberParameter("Falloff", "Fo", "Soft edge (m)", GH_ParamAccess.item, 8.0);
            p.AddNumberParameter("Radius", "R", "Points' radius (m)", GH_ParamAccess.item, 12.0);
            p.AddNumberParameter("Width", "W", "Open curves' width (m)", GH_ParamAccess.item, 6.0);
            p.AddNumberParameter("Downhill", "D", "0 = the flow stays; 1 = it runs down the new slopes", GH_ParamAccess.item, 0.0);
            p[1].Optional = true; p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
            => p.AddParameter(new FieldParam(), "Field", "F", "The sculpted field", GH_ParamAccess.item);

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            var areas = new List<Curve>();
            var points = new List<Point3d>();
            double height = -4, falloff = 8, radius = 12, width = 6, downhill = 0;
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            DA.GetDataList(1, areas); DA.GetDataList(2, points);
            DA.GetData(3, ref height); DA.GetData(4, ref falloff); DA.GetData(5, ref radius); DA.GetData(6, ref width); DA.GetData(7, ref downhill);
            var f = g.Value;
            falloff = Math.Max(falloff, 1e-3);
            var w = new double[f.Count];
            var closed = areas.Where(c => c != null && c.IsClosed).Select(c => Flat(c)).Where(c => c != null).ToList();
            var open = areas.Where(c => c != null && !c.IsClosed).Select(c => Flat(c)).Where(c => c != null).ToList();
            for (int k = 0; k < f.Count; k++)
            {
                if (!f.Has[k]) continue;
                var (x, y, _) = f.CellWorld(k);
                var q = new Point3d(x, y, 0);
                double best = 0;
                foreach (var c in closed)
                {
                    if (c.Contains(q, Plane.WorldXY, 1e-6) != PointContainment.Inside) continue;
                    c.ClosestPoint(q, out double t);
                    best = Math.Max(best, Smooth(q.DistanceTo(c.PointAt(t)) / falloff));
                }
                foreach (var c in open)
                {
                    c.ClosestPoint(q, out double t);
                    double d = q.DistanceTo(c.PointAt(t));
                    best = Math.Max(best, Smooth((width / 2 + falloff - d) / falloff));
                }
                foreach (var p in points)
                {
                    double d = q.DistanceTo(new Point3d(p.X, p.Y, 0));
                    best = Math.Max(best, Smooth((radius - d) / Math.Min(falloff, radius)));
                }
                w[k] = best;
            }
            DA.SetData(0, new FieldGoo(Shape.Sculpt(f, w, height, Math.Max(0, Math.Min(1, downhill)))));
            Message = $"{w.Count(v => v > 0)} cells";
        }

        static double Smooth(double x)
        {
            x = Math.Max(0, Math.Min(1, x));
            return x * x * (3 - 2 * x);
        }

        static Curve Flat(Curve c)
        {
            var d = c.DuplicateCurve();
            return d.Transform(Transform.PlanarProjection(Plane.WorldXY)) ? d : null;
        }
    }

    internal static class JoinTools
    {
        /// <summary>A join function over every pair of fields (each pair once).</summary>
        public static List<FieldData> Pairs(List<FieldData> fields, Func<FieldData, FieldData, List<FieldData>> join)
        {
            var o = new List<FieldData>();
            for (int a = 0; a < fields.Count; a++)
                for (int b = a + 1; b < fields.Count; b++)
                    o.AddRange(join(fields[a], fields[b]));
            return o;
        }

        /// <summary>Edge points inside closed curves or within width of open ones (seen from above).</summary>
        public static Func<double[], bool> Mask(List<Curve> where, double width)
        {
            var flat = where.Where(c => c != null).Select(c =>
            {
                var d = c.DuplicateCurve();
                d.Transform(Transform.PlanarProjection(Plane.WorldXY));
                return d;
            }).ToList();
            return p =>
            {
                var q = new Point3d(p[0], p[1], 0);
                foreach (var c in flat)
                {
                    if (c.IsClosed && c.Contains(q, Plane.WorldXY, 1e-6) == PointContainment.Inside) return true;
                    if (!c.IsClosed && c.ClosestPoint(q, out double t) && q.DistanceTo(c.PointAt(t)) <= width) return true;
                }
                return false;
            };
        }
    }
}
