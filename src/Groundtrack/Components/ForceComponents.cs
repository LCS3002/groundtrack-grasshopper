using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack.Components
{
    /// <summary>Base of the force components: the Force output, an optional Layers input (which
    /// layers of a Particles component it acts on) and a light grey preview of its geometry.</summary>
    public abstract class ForceComponentBase : GtComponent
    {
        protected ForceComponentBase(string name, string nick, string desc) : base(name, nick, desc, Tab.Forces) { }
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected static readonly Color Guide = Color.FromArgb(150, 150, 155);
        protected int LayersIndex = -1;

        protected void AddLayers(GH_InputParamManager p)
        {
            LayersIndex = p.AddIntegerParameter("Layers", "Ly", "Only these layers of the Particles (0 = its first field, 1 = its second ...); empty = all", GH_ParamAccess.list);
            p[LayersIndex].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
            => p.AddParameter(new ForceParam(), "Force", "F", "Plug into Particles > Forces (several forces: plug them all in)", GH_ParamAccess.item);

        protected void Output(IGH_DataAccess DA, ForceBase f)
        {
            if (LayersIndex >= 0)
            {
                var ly = new List<int>();
                if (DA.GetDataList(LayersIndex, ly)) f.Layers = ly.ToArray();
            }
            DA.SetData(0, new ForceGoo(f));
        }
    }

    public class ForceComponent : ForceComponentBase
    {
        public ForceComponent() : base("Force", "Force",
            "POP Force: a constant push (vector, m/s^2) plus smooth turbulence (Noise) that changes over space (Scale) and time (Speed).") { }
        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e511");
        protected override Bitmap Icon => Icons.Get("force");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddVectorParameter("Force", "F", "Constant acceleration (m/s^2), e.g. (0, 0, 0.5) lifts", GH_ParamAccess.item, Vector3d.Zero);
            p.AddNumberParameter("Noise", "N", "Turbulence strength (m/s^2)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Scale", "Sc", "Size of the turbulence swirls (m)", GH_ParamAccess.item, 10.0);
            p.AddNumberParameter("Speed", "Sp", "How fast the turbulence changes", GH_ParamAccess.item, 0.5);
            p.AddIntegerParameter("Seed", "Seed", "Turbulence seed", GH_ParamAccess.item, 1);
            AddLayers(p);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var v = Vector3d.Zero;
            double noise = 0, scale = 10, speed = 0.5;
            int seed = 1;
            DA.GetData(0, ref v); DA.GetData(1, ref noise); DA.GetData(2, ref scale); DA.GetData(3, ref speed); DA.GetData(4, ref seed);
            Output(DA, new UniformForce { Fx = v.X, Fy = v.Y, Fz = v.Z, Noise = noise, Scale = scale, Speed = speed, Seed = seed });
        }
    }

    public class AxisForceComponent : ForceComponentBase
    {
        public AxisForceComponent() : base("Axis Force", "Axis",
            "POP Axis Force: particles orbit around an axis (Orbit, m/s^2; negative = the other way), are sucked towards it (Suction) and lifted along it (Lift), within Radius.") { }
        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e512");
        protected override Bitmap Icon => Icons.Get("axis");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddLineParameter("Axis", "A", "The axis (a line; its direction sets which way is up)", GH_ParamAccess.item, new Line(Point3d.Origin, Vector3d.ZAxis * 10));
            p.AddNumberParameter("Radius", "R", "Reach of the force (m); 0 = everywhere", GH_ParamAccess.item, 20.0);
            p.AddNumberParameter("Orbit", "O", "Swirl around the axis (m/s^2)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Suction", "Su", "Pull towards the axis (m/s^2); negative pushes out", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Lift", "Li", "Push along the axis (m/s^2)", GH_ParamAccess.item, 0.0);
            AddLayers(p);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var a = new Line(Point3d.Origin, Vector3d.ZAxis * 10);
            double r = 20, orbit = 1, suction = 0, lift = 0;
            DA.GetData(0, ref a); DA.GetData(1, ref r); DA.GetData(2, ref orbit); DA.GetData(3, ref suction); DA.GetData(4, ref lift);
            var d = a.Direction;
            Output(DA, new AxisForce { Ox = a.From.X, Oy = a.From.Y, Oz = a.From.Z, Dx = d.X, Dy = d.Y, Dz = d.Z, Radius = r, Orbit = orbit, Suction = suction, Lift = lift });
            Buckets.Add(a, Guide);
            if (r > 0)
            {
                var c = new Circle(new Plane(a.From, d), r);
                var pl = c.ToNurbsCurve().ToPolyline(0, 0, 0.1, 0, 0, 0.01, 0, 0, true);
                if (pl != null && pl.TryGetPolyline(out Polyline poly))
                    for (int k = 1; k < poly.Count; k++) Buckets.Add(new Line(poly[k - 1], poly[k]), Guide);
            }
        }
    }

    public class AttractComponent : ForceComponentBase
    {
        public AttractComponent() : base("Attract", "Attract",
            "POP Attract: particles pulled towards points (Strength > 0) or pushed away (< 0), fading out at Radius.") { }
        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e513");
        protected override Bitmap Icon => Icons.Get("attract");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddPointParameter("Points", "P", "Attractors (or repellers)", GH_ParamAccess.list);
            p.AddNumberParameter("Strength", "S", "m/s^2: positive attracts, negative repels", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Radius", "R", "Reach (m); 0 = everywhere", GH_ParamAccess.item, 10.0);
            p.AddBooleanParameter("Plan", "Pl", "Pull in plan only (keep heights)", GH_ParamAccess.item, false);
            AddLayers(p);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var pts = new List<Point3d>();
            double s = 1, r = 10;
            bool plan = false;
            if (!DA.GetDataList(0, pts)) return;
            DA.GetData(1, ref s); DA.GetData(2, ref r); DA.GetData(3, ref plan);
            var f = new PointForce { Strength = s, Radius = r, Plan = plan };
            foreach (var q in pts) { f.Points.Add((q.X, q.Y, q.Z)); Cloud.Add(q, Guide); }
            Output(DA, f);
        }
    }

    public class CurveForceComponent : ForceComponentBase
    {
        public CurveForceComponent() : base("Curve Force", "CrvF",
            "Curves as a force: follow (flow along them, in their direction), attract (pull onto them) or repel, within Radius.") { }
        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e514");
        protected override Bitmap Icon => Icons.Get("curveforce");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Curves", "C", "Curves (their direction counts for follow)", GH_ParamAccess.list);
            p.AddTextParameter("Mode", "M", "follow / attract / repel", GH_ParamAccess.item, "follow");
            p.AddNumberParameter("Strength", "S", "m/s^2", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Radius", "R", "Reach (m)", GH_ParamAccess.item, 5.0);
            AddLayers(p);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var cs = new List<Curve>();
            string mode = "follow";
            double s = 1, r = 5;
            if (!DA.GetDataList(0, cs)) return;
            DA.GetData(1, ref mode); DA.GetData(2, ref s); DA.GetData(3, ref r);
            var kind = (mode ?? "").Trim().ToLowerInvariant().StartsWith("a") ? CurveForce.Kind.Attract
                     : (mode ?? "").Trim().ToLowerInvariant().StartsWith("r") ? CurveForce.Kind.Repel : CurveForce.Kind.Follow;
            Output(DA, new CurveForce(cs.Select(c => Geo.Poly(c))) { Mode = kind, Strength = s, Radius = r });
            Message = kind.ToString().ToLowerInvariant();
        }
    }

    public class FieldForceComponent : ForceComponentBase
    {
        public FieldForceComponent() : base("Field Force", "FldF",
            "A movement field as a force (the vectors as wind): particles of any layer are pulled towards this field's velocity where it has data, " +
            "and (Height pull) towards its height - so one layer's flow drags the other's particles over.") { }
        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e515");
        protected override Bitmap Icon => Icons.Get("fieldforce");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field (from Field, Merge, Blend, Field from Curves)", GH_ParamAccess.item);
            p.AddNumberParameter("Strength", "S", "1/s: how quickly particles take on this field's velocity", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Height pull", "Hp", "1/s^2: pull towards this field's height (stack fields with Move Z)", GH_ParamAccess.item, 0.0);
            AddLayers(p);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            double s = 1, h = 0;
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            DA.GetData(1, ref s); DA.GetData(2, ref h);
            Output(DA, new FieldForce { Field = g.Value, Strength = s, HeightPull = h });
        }
    }

    public class InteractComponent : ForceComponentBase
    {
        public InteractComponent() : base("Interact", "Interact",
            "POP Interact / flocking: particles within Radius push apart (Separation), pull together (Cohesion) and match velocity (Alignment). " +
            "Between: others (layers pull on each other - they intertwine), same (each layer flocks), all.") { }
        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e516");
        protected override Bitmap Icon => Icons.Get("interact");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Between", "B", "others / same / all", GH_ParamAccess.item, "others");
            p.AddNumberParameter("Radius", "R", "Neighbourhood (m)", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Separation", "Se", "Push apart (m/s^2)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Cohesion", "Co", "Pull together (1/s^2)", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Alignment", "Al", "Match velocity (1/s)", GH_ParamAccess.item, 0.5);
            AddLayers(p);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string b = "others";
            double r = 3, se = 1, co = 0.5, al = 0.5;
            DA.GetData(0, ref b); DA.GetData(1, ref r); DA.GetData(2, ref se); DA.GetData(3, ref co); DA.GetData(4, ref al);
            string bl = (b ?? "").Trim().ToLowerInvariant();
            var who = bl.StartsWith("s") ? InteractForce.Who.Same : bl.StartsWith("a") ? InteractForce.Who.All : InteractForce.Who.Others;
            Output(DA, new InteractForce { Between = who, Radius = r, Separation = se, Cohesion = co, Alignment = al });
            Message = who.ToString().ToLowerInvariant();
        }
    }

    public class ObstacleComponent : ForceComponentBase
    {
        public ObstacleComponent() : base("Obstacle", "Obst",
            "POP Collision: particles keep out of geometry - closed curves (footprints, up to Height) or breps / meshes (massing). " +
            "Pushed away within Margin; if they get in, they bounce off (Bounce 0..1) or die (Kill).") { }
        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e517");
        protected override Bitmap Icon => Icons.Get("obstacle");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGeometryParameter("Geometry", "G", "Closed curves (plan footprints), breps or meshes", GH_ParamAccess.list);
            p.AddNumberParameter("Height", "H", "Footprints: up to this height (empty = all heights)", GH_ParamAccess.item);
            p.AddNumberParameter("Margin", "M", "Start pushing this far away (m)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Bounce", "B", "0 = slide along, 1 = bounce back", GH_ParamAccess.item, 0.3);
            p.AddBooleanParameter("Kill", "K", "Kill particles that hit it", GH_ParamAccess.item, false);
            p[1].Optional = true;
            AddLayers(p);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var geo = new List<IGH_GeometricGoo>();
            double h = double.NaN, margin = 1, bounce = 0.3;
            bool kill = false;
            if (!DA.GetDataList(0, geo)) return;
            DA.GetData(1, ref h); DA.GetData(2, ref margin); DA.GetData(3, ref bounce); DA.GetData(4, ref kill);
            var f = new ObstacleForce { Margin = margin, Bounce = bounce, Kill = kill };
            int skipped = 0;
            foreach (var g in geo)
            {
                switch (g)
                {
                    case GH_Curve c when c.Value != null && c.Value.IsClosed:
                        var pts = Geo.Poly(c.Value, 0.5);
                        double z0 = pts.Min(q => q.Z);
                        f.Obstacles.Add(new FootprintObstacle(pts.Select(q => (q.X, q.Y)).ToList())
                        {
                            ZMin = double.IsNaN(h) ? double.NegativeInfinity : z0 - 0.5,
                            ZMax = double.IsNaN(h) ? double.PositiveInfinity : z0 + h,
                        });
                        for (int k = 1; k < pts.Count; k++)
                            Buckets.Add(new Line(new Point3d(pts[k - 1].X, pts[k - 1].Y, pts[k - 1].Z), new Point3d(pts[k].X, pts[k].Y, pts[k].Z)), Guide);
                        break;
                    case GH_Mesh m when m.Value != null:
                        f.Obstacles.Add(new MeshObstacle(m.Value));
                        break;
                    case GH_Brep b when b.Value != null:
                        AddBrep(f, b.Value);
                        break;
                    case GH_Surface s when s.Value != null:
                        AddBrep(f, s.Value);
                        break;
                    default:
                        if (g != null && g.CastTo(out Brep br) && br != null) AddBrep(f, br);
                        else skipped++;
                        break;
                }
            }
            if (skipped > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{skipped} items ignored (open curves or unsupported geometry)");
            Output(DA, f);
            Message = $"{f.Obstacles.Count} obstacles";
        }

        static void AddBrep(ObstacleForce f, Brep b)
        {
            var parts = Mesh.CreateFromBrep(b, MeshingParameters.FastRenderMesh);
            if (parts == null || parts.Length == 0) return;
            var m = new Mesh();
            foreach (var p in parts) m.Append(p);
            f.Obstacles.Add(new MeshObstacle(m));
        }
    }
}
