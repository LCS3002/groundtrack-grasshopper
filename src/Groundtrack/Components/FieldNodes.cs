using System;
using System.Drawing;
using Grasshopper.Kernel;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack.Components
{
    /// <summary>Place a field: turn (degrees, about its own centre), scale, stretch, mirror -
    /// in the field's own plane (tilted fields stay tilted) - then move (Z stacks them).</summary>
    public class TransformFieldComponent : GH_Component
    {
        public TransformFieldComponent()
            : base("Transform Field", "XField",
                   "Place a field: Rotate (degrees, about its own centre or Centre), Scale, Stretch X / Y, Mirror - in the " +
                   "field's own plane - then Move (Z stacks fields as layers). To tilt or stand a field up use Grasshopper's " +
                   "Rotate 3D / Rotate Axis / Orient. Also outputs the transform, for other geometry.",
                   Tab.Name, Tab.Data) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e518");
        protected override Bitmap Icon => Icons.Get("transformfield");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field", GH_ParamAccess.item);
            p.AddVectorParameter("Move", "M", "Move (m); Z lifts it (stack layers)", GH_ParamAccess.item, Vector3d.Zero);
            p.AddNumberParameter("Rotate", "R", "Turn (degrees, counter-clockwise seen from above)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Scale", "S", "Uniform scale (speeds scale with it)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Stretch X", "Sx", "Stretch along X (before turning)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Stretch Y", "Sy", "Stretch along Y (before turning)", GH_ParamAccess.item, 1.0);
            p.AddBooleanParameter("Mirror", "Mi", "Mirror (flip left-right before turning)", GH_ParamAccess.item, false);
            p.AddPointParameter("Centre", "C", "Turn / scale about this point; empty = the field's centre", GH_ParamAccess.item);
            p[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "The placed field", GH_ParamAccess.item);
            p.AddTransformParameter("Transform", "X", "The same transform (Transform node: apply it to other geometry)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            var move = Vector3d.Zero;
            double rot = 0, scale = 1, sx = 1, sy = 1;
            bool mirror = false;
            var centre = Point3d.Unset;
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            DA.GetData(1, ref move); DA.GetData(2, ref rot); DA.GetData(3, ref scale);
            DA.GetData(4, ref sx); DA.GetData(5, ref sy); DA.GetData(6, ref mirror);
            bool hasCentre = DA.GetData(7, ref centre);
            var f = g.Value;
            var pl = f.Place;
            // the centre in the field's plane (= world for flat fields)
            double cx, cy, cz;
            if (hasCentre) (cx, cy, cz) = pl.ToPlane(centre.X, centre.Y, centre.Z);
            else
            {
                var (x0, y0, x1, y1) = f.Bounds();
                cx = (x0 + x1) / 2; cy = (y0 + y1) / 2; cz = 0;
            }
            double a = rot * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), mx = mirror ? -1 : 1;
            double a00 = scale * c * mx * sx, a01 = -scale * s * sy, a10 = scale * s * mx * sx, a11 = scale * c * sy;
            var inPlane = new[,]
            {
                { a00, a01, 0, cx - (a00 * cx + a01 * cy) },
                { a10, a11, 0, cy - (a10 * cx + a11 * cy) },
                { 0, 0, scale, cz - scale * cz },
            };
            var x = pl.PlaneToWorld(inPlane);
            x[0, 3] += move.X; x[1, 3] += move.Y; x[2, 3] += move.Z;
            var o = f.Clone();
            o.Place = pl.Then(x);
            var xf = Transform.Identity;
            for (int i = 0; i < 3; i++) for (int j = 0; j < 4; j++) xf[i, j] = x[i, j];
            DA.SetData(0, new FieldGoo(o));
            DA.SetData(1, xf);
            Message = rot != 0 ? $"{rot:0.#} deg" : "";
        }
    }

    /// <summary>Smooth a field (Gaussian), optionally closing small gaps.</summary>
    public class SmoothFieldComponent : GH_Component
    {
        public SmoothFieldComponent()
            : base("Smooth Field", "Smooth",
                   "Smooth a field over Radius metres: velocities and heights relax into one another (like subdividing a mesh), " +
                   "busy cells hold their direction. Fill closes gaps up to about the radius.",
                   Tab.Name, Tab.Flow) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e519");
        protected override Bitmap Icon => Icons.Get("smooth");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field", GH_ParamAccess.item);
            p.AddNumberParameter("Radius", "R", "How far it smooths (m)", GH_ParamAccess.item, 3.0);
            p.AddBooleanParameter("Fill", "Fill", "Close gaps (cells without data near data get the smoothed flow)", GH_ParamAccess.item, false);
            p.AddNumberParameter("Max slope", "MS", "Steepest the ground may get (rise per run; 1 = 45 deg); 0 = off", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
            => p.AddParameter(new FieldParam(), "Field", "F", "The smoothed field", GH_ParamAccess.item);

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            double r = 3;
            bool fill = false;
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            double maxSlope = 0;
            DA.GetData(1, ref r); DA.GetData(2, ref fill); DA.GetData(3, ref maxSlope);
            var o = FieldTools.Smooth(FieldTools.LimitSlope(g.Value, Math.Max(0, maxSlope)), Math.Max(0, r), fill);
            DA.SetData(0, new FieldGoo(o));
            Message = $"{o.Valid} cells";
        }
    }
}
