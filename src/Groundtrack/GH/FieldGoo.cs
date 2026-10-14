using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack
{
    /// <summary>A movement field in Grasshopper. Transform nodes (Move, Rotate, Scale, Orient)
    /// place it; morph nodes (Bend, Twist, Taper, Box Morph, Surface Morph ...) bend it into 3D.
    /// Its velocities turn, scale and bend with it.</summary>
    public class FieldGoo : GH_GeometricGoo<FieldData>, IGH_PreviewData
    {
        public FieldGoo() { }
        public FieldGoo(FieldData f) : base(f) { }

        public override string TypeName => "Field";
        public override string TypeDescription => "A groundtrack movement field (grid of velocities)";
        public override bool IsValid => Value != null && Value.Count > 0;

        public override string ToString()
            => Value == null ? "no field"
               : $"{Value.Name}: {Value.Valid} cells of {Value.WorldCell:0.##} m";

        public override IGH_GeometricGoo DuplicateGeometry() => new FieldGoo(Value?.Clone());

        public override BoundingBox Boundingbox => Box(Value);

        public override BoundingBox GetBoundingBox(Transform xform)
        {
            var b = Box(Value);
            b.Transform(xform);
            return b;
        }

        static BoundingBox Box(FieldData f)
        {
            if (f == null || f.Valid == 0) return BoundingBox.Empty;
            var (x0, y0, z0, x1, y1, z1) = f.WorldBox();
            return new BoundingBox(x0, y0, z0, x1, y1, z1);
        }

        public override IGH_GeometricGoo Transform(Transform x)
        {
            if (Value == null) return null;
            var f = Value.Clone();
            // plan transforms stay plan placements; tilting ones (Rotate about X / Y, Rotate 3D,
            // Orient to a vertical plane) tilt the field's plane, its flow along with it
            var m = new double[3, 4];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 4; j++) m[i, j] = x[i, j];
            f.Place = f.Place.Then(m);
            if (f.Warp != null) f.Warp = f.Warp.Transformed(m);
            return new FieldGoo(f);
        }

        /// <summary>Bent by a space morph: the field becomes a curved sheet, its flow along it.</summary>
        public override IGH_GeometricGoo Morph(SpaceMorph xmorph)
        {
            if (Value == null || xmorph == null) return null;
            return new FieldGoo(Value.Morphed((x, y, z) =>
            {
                var q = xmorph.MorphPoint(new Point3d(x, y, z));
                return (q.X, q.Y, q.Z);
            }));
        }

        public override bool CastTo<Q>(ref Q target)
        {
            if (typeof(Q).IsAssignableFrom(typeof(FieldData)))
            {
                target = (Q)(object)Value;
                return true;
            }
            return false;
        }

        public override bool CastFrom(object source)
        {
            if (source is FieldData f) { Value = f; return true; }
            if (source is FieldGoo g) { Value = g.Value; return true; }
            return false;
        }

        // ---- preview: arrows in the speed colours (a few thousand at most)
        public BoundingBox ClippingBox => Boundingbox;

        List<(Line[] Lines, Color Colour)> _arrows;

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (Value == null) return;
            _arrows = _arrows ?? Arrows(Value, 1500);
            double dpi = args.Pipeline.DpiScale > 0 ? args.Pipeline.DpiScale : 1;
            int th = Math.Max(1, (int)Math.Round(1.5 * dpi * Preview.CaptureScale));
            foreach (var (lines, c) in _arrows) args.Pipeline.DrawLines(lines, Color.FromArgb(160, c), th);
        }

        public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }

        /// <summary>Short arrows (1 cell long at the top speed) on every n-th cell.</summary>
        public static List<(Line[] Lines, Color Colour)> Arrows(FieldData f, int max)
        {
            var buckets = new Preview.LineBuckets();
            int stride = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)f.Valid / max)));
            double cell = f.WorldCell;
            for (int j = 0; j < f.Ny; j += stride)
                for (int i = 0; i < f.Nx; i += stride)
                {
                    int k = j * f.Nx + i;
                    if (!f.Has[k]) continue;
                    var (x, y, z) = f.CellWorld(k);
                    var (vx, vy, vz) = f.CellVelocity(k);
                    double sp = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    if (sp < 1e-6) continue;
                    double top = Math.Max(f.Smax[k] * f.Scale, 1e-6);
                    double len = cell * stride * 0.9 * Math.Min(1, 0.35 + sp / top);
                    var u = new Vector3d(vx / sp, vy / sp, vz / sp);
                    var a = new Point3d(x, y, z) - u * len / 2;
                    var b = new Point3d(x, y, z) + u * len / 2;
                    var c = Preview.SpeedColour(sp / f.Scale, f.Smax[k]);
                    buckets.Add(new Line(a, b), c);
                    // arrow head, in the field's plane
                    var d = u * len * 0.3;
                    var (nx, ny, nz) = f.CellNormal(k);
                    var side = Vector3d.CrossProduct(new Vector3d(nx, ny, nz), d) * 0.5;
                    buckets.Add(new Line(b, b - d + side), c);
                    buckets.Add(new Line(b, b - d - side), c);
                }
            return buckets.ToList();
        }
    }

    /// <summary>The Field parameter.</summary>
    public class FieldParam : GH_Param<FieldGoo>, IGH_PreviewObject
    {
        public FieldParam() : base("Field", "F", "A groundtrack movement field", Tab.Name, Tab.Data, GH_ParamAccess.item) { }
        public override Guid ComponentGuid => new Guid("b1b0f6a4-5d4b-4f43-9f3a-1b4f3f6e2a10");
        protected override Bitmap Icon => Icons.Get("field");
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        public bool Hidden { get; set; }
        public bool IsPreviewCapable => true;
        public BoundingBox ClippingBox => Preview_ComputeClippingBox();
        public void DrawViewportWires(IGH_PreviewArgs args) => Preview_DrawWires(args);
        public void DrawViewportMeshes(IGH_PreviewArgs args) { }
    }
}
