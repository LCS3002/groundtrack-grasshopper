using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack.Components
{
    /// <summary>The real tracks over time: every tracked person / vehicle at time T, with its
    /// trail - placed with its field (Move / Rotate / Scale the field, the replay follows).</summary>
    public class ReplayComponent : GtComponent
    {
        public ReplayComponent()
            : base("Replay", "Replay",
                   "The real tracks behind a field at time T: points + trails, coloured by speed. Connect a Timeline to T.",
                   Tab.Time) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e507");
        protected override Bitmap Icon => Icons.Get("replay");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override float PointSize => 4;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field from the Field component (its tracks are replayed)", GH_ParamAccess.item);
            p.AddNumberParameter("Time", "T", "Time (s) - connect a Timeline", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Tail", "Tl", "Trail length (s)", GH_ParamAccess.item, 3.0);
            p.AddTextParameter("Group", "G", "Only this group (people, vehicles ...); empty = the field's group", GH_ParamAccess.item);
            p.AddParameter(new GradientParam(), "Gradient", "Grad", "Recolour (Gradient component); empty = the groundtrack ramp", GH_ParamAccess.item);
            p[3].Optional = true;
            p[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter("Points", "P", "Where everyone is at T", GH_ParamAccess.list);
            p.AddVectorParameter("Velocity", "V", "Their velocities (m/s)", GH_ParamAccess.list);
            p.AddNumberParameter("Speed", "Sp", "Their speeds (m/s)", GH_ParamAccess.list);
            p.AddColourParameter("Colour", "Col", "Speed colours", GH_ParamAccess.list);
            p.AddCurveParameter("Trails", "Tr", "Their paths over the last Tail seconds", GH_ParamAccess.list);
            p.AddIntegerParameter("Id", "Id", "Track ids", GH_ParamAccess.list);
            p.AddIntervalParameter("Range", "R", "Time range of the tracks (s)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            double t = 0, tail = 3;
            string group = null;
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            DA.GetData(1, ref t);
            DA.GetData(2, ref tail);
            DA.GetData(3, ref group);
            GradientGoo grad = null;
            DA.GetData(4, ref grad);
            var gradient = grad?.Value;
            var f = g.Value;
            if (string.IsNullOrEmpty(f.TracksPath) || !File.Exists(f.TracksPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "This field has no tracks (a blend, or points.csv missing)");
                return;
            }
            var tr = TrackSet.Load(f.TracksPath, f.OriginE, f.OriginN, f.Z0, f.Tops);
            string gname = string.IsNullOrEmpty(group) ? f.Group : group;
            int gi = gname == "all" || string.IsNullOrEmpty(gname) ? -1 : Array.IndexOf(tr.GroupNames, gname);
            if (gi < 0 && !(gname == "all" || string.IsNullOrEmpty(gname)))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"No '{gname}' in these tracks");
                return;
            }
            var pl = f.Place;
            double scale = Math.Max(f.Scale, 1e-9);
            var pts = new List<Point3d>();
            var vel = new List<Vector3d>();
            var spd = new List<double>();
            var col = new List<Color>();
            var trails = new List<Curve>();
            var ids = new List<int>();
            foreach (var s in tr.At(t, gi))
            {
                var (x, y, z) = pl.Apply(s.X, s.Y, s.Z);
                var (vx, vy, vz) = pl.ApplyVector3(s.Vx, s.Vy);
                var c = Preview.SpeedColour(s.Speed, s.Top, gradient);
                pts.Add(new Point3d(x, y, z));
                vel.Add(new Vector3d(vx, vy, vz));
                spd.Add(s.Speed);
                col.Add(c);
                ids.Add(s.Id);
                Cloud.Add(new Point3d(x, y, z), c);
                if (tail > 0)
                {
                    var path = tr.Trail(s.Track, t - tail, t);
                    if (path.Count >= 2)
                    {
                        var tp = path.Select(q => { var (a, b, cz) = pl.Apply(q.X, q.Y, q.Z); return new Point3d(a, b, cz); }).ToList();
                        trails.Add(new PolylineCurve(tp));
                        Buckets.AddPolyline(tp, path.Select(q => q.Speed).ToList(), s.Top, gradient);
                        BakeCurves.Add(trails[trails.Count - 1]);
                        BakeColours.Add(c);
                    }
                }
            }
            BakePts.AddRange(pts);
            BakePtColours.AddRange(col);
            DA.SetDataList(0, pts);
            DA.SetDataList(1, vel);
            DA.SetDataList(2, spd);
            DA.SetDataList(3, col);
            DA.SetDataList(4, trails);
            DA.SetDataList(5, ids);
            DA.SetData(6, new Interval(tr.TMin, tr.TMax));
            Message = $"{pts.Count} at {t:0.0} s";
        }
    }
}
