using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack
{
    /// <summary>A force for the Particles component (POP-like).</summary>
    public class ForceGoo : GH_Goo<IForce>
    {
        public ForceGoo() { }
        public ForceGoo(IForce f) : base(f) { }
        public override bool IsValid => Value != null;
        public override string TypeName => "Force";
        public override string TypeDescription => "A force on groundtrack particles";
        public override IGH_Goo Duplicate() => new ForceGoo(Value);
        public override string ToString() => Value is ForceBase b ? b.Key.Split(' ')[0] + " force" : "force";

        public override bool CastFrom(object source)
        {
            if (source is IForce f) { Value = f; return true; }
            return false;
        }
    }

    public class ForceParam : GH_Param<ForceGoo>
    {
        public ForceParam() : base("Force", "Frc", "A force on groundtrack particles", Tab.Name, Tab.Forces, GH_ParamAccess.item) { }
        public override Guid ComponentGuid => new Guid("b1b0f6a4-5d4b-4f43-9f3a-1b4f3f6e2a11");
        protected override Bitmap Icon => Icons.Get("force");
        public override GH_Exposure Exposure => GH_Exposure.hidden;
    }

    /// <summary>A colour gradient (t 0..1 -> colour).</summary>
    public class GradientGoo : GH_Goo<Gradient>
    {
        public GradientGoo() { }
        public GradientGoo(Gradient g) : base(g) { }
        public override bool IsValid => Value != null;
        public override string TypeName => "Gradient";
        public override string TypeDescription => "A colour gradient for groundtrack previews";
        public override IGH_Goo Duplicate() => new GradientGoo(Value);
        public override string ToString() => $"gradient ({Value?.Name}, {Value?.Stops.Length} stops)";

        public override bool CastFrom(object source)
        {
            switch (source)
            {
                case Gradient g: Value = g; return true;
                case GH_String s: Value = Gradient.Preset(s.Value); return true;
                case string s: Value = Gradient.Preset(s); return true;
            }
            return false;
        }
    }

    public class GradientParam : GH_Param<GradientGoo>
    {
        public GradientParam() : base("Gradient", "Grad", "A colour gradient", Tab.Name, Tab.Colour, GH_ParamAccess.item) { }
        public override Guid ComponentGuid => new Guid("b1b0f6a4-5d4b-4f43-9f3a-1b4f3f6e2a12");
        protected override Bitmap Icon => Icons.Get("gradient");
        public override GH_Exposure Exposure => GH_Exposure.hidden;
    }

    /// <summary>A Rhino mesh (or meshed brep) as an obstacle: signed distance by closest point,
    /// inside by the mesh's own test (closed meshes).</summary>
    public sealed class MeshObstacle : IObstacle
    {
        readonly Mesh _m;
        readonly bool _closed;
        readonly string _key;

        public MeshObstacle(Mesh m)
        {
            _m = m.DuplicateMesh();
            _m.Normals.ComputeNormals();
            _closed = _m.IsClosed;
            var b = _m.GetBoundingBox(false);
            _key = $"mesh {_m.Vertices.Count},{b.Min},{b.Max}";
        }

        public string Key => _key;

        public double SignedDistance(double x, double y, double z, out double nx, out double ny, out double nz)
        {
            nx = ny = nz = 0;
            var p = new Point3d(x, y, z);
            var mp = _m.ClosestMeshPoint(p, 0);
            if (mp == null) return double.PositiveInfinity;
            var q = mp.Point;
            var d = p - q;
            double dist = d.Length;
            bool inside = _closed && _m.IsPointInside(p, 1e-6, true);
            if (dist < 1e-9)
            {
                var n = _m.NormalAt(mp);
                nx = n.X; ny = n.Y; nz = n.Z;
                return 0;
            }
            d.Unitize();
            if (inside) { nx = -d.X; ny = -d.Y; nz = -d.Z; return -dist; }
            nx = d.X; ny = d.Y; nz = d.Z;
            return dist;
        }
    }
}
