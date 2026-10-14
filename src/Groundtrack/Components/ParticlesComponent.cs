using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Groundtrack.Core;
using Rhino.Geometry;
using ParticleSystem = Groundtrack.Core.ParticleSystem;

namespace Groundtrack.Components
{
    /// <summary>A particle simulation through one or more fields (layers), like a Houdini POP
    /// network, with forces. With a Time input (Timeline) it is a function of time: scrub back
    /// and forth. Without, Run plays it.</summary>
    public class ParticlesComponent : GtComponent
    {
        public ParticlesComponent()
            : base("Particles", "POP",
                   "Particles born where people / vehicles were (busier places more), following their field and pushed by Forces. " +
                   "Several fields = several layers (stack them with Move Z; Interact makes layers pull on each other). " +
                   "Connect a Timeline to Time (scrub / play / record), or use Run.", Tab.Flow) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e503");
        protected override Bitmap Icon => Icons.Get("particles");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override float PointSize => 3;

        ParticleSystem _sys;
        double _clock;
        DateTime _lastTick = DateTime.MinValue;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Fields", "F", "One or more fields: each is a layer with its own particles", GH_ParamAccess.list);
            p.AddNumberParameter("Time", "T", "Time (s) - connect a Timeline; empty: Run plays in real time", GH_ParamAccess.item);
            p.AddBooleanParameter("Run", "Run", "Play (when Time is empty)", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Reset", "Rst", "Back to the start", GH_ParamAccess.item, false);
            p.AddNumberParameter("Rate", "R", "Births per second (per layer)", GH_ParamAccess.item, 300);
            p.AddNumberParameter("Life", "L", "Life (s); empty: long enough to cross the field", GH_ParamAccess.item);
            p.AddNumberParameter("Speed", "Sp", "Speed factor (1 = as measured)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Noise", "N", "Wandering 0..1 (share of the local speed)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Trail", "Tr", "Trail length (s)", GH_ParamAccess.item, 2.0);
            p.AddIntegerParameter("Seed", "Seed", "Random seed", GH_ParamAccess.item, 1);
            p.AddParameter(new ForceParam(), "Forces", "Frc", "Forces (Force, Axis Force, Attract, Curve Force, Field Force, Interact, Obstacle) - plug in any number", GH_ParamAccess.list);
            p.AddNumberParameter("Response", "Rs", "0 = take the field's velocity (follow exactly); > 0 = inertia: reach it within about this many seconds (forces bend the flow more)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Hold", "H", "Keep to the layer's ground: 1 = on it, 0.2 = loosely, 0 = free in Z", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Drag", "D", "How fast pushes from forces die out (1/s)", GH_ParamAccess.item, 1.0);
            p.AddBooleanParameter("Outside", "Out", "Keep particles that leave their field (they coast on, forces only)", GH_ParamAccess.item, false);
            p.AddTextParameter("Colour", "Col", "Colour by: speed / layer / age", GH_ParamAccess.item, "speed");
            p.AddParameter(new GradientParam(), "Gradient", "G", "Recolour (Gradient component); empty = the groundtrack ramp", GH_ParamAccess.item);
            p.AddNumberParameter("Join", "J", "Particles leaving their field carry on onto another field (layer) whose ground is within this " +
                                 "distance (m): fields meeting edge to edge (floor, slope, wall - tilt them with Rotate 3D) are one " +
                                 "continuous surface. 0 = off", GH_ParamAccess.item, 2.0);
            p[1].Optional = true;
            p[5].Optional = true;
            p[10].Optional = true;
            p[16].Optional = true;
            AddWidth(p);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter("Points", "P", "Particle positions", GH_ParamAccess.list);
            p.AddVectorParameter("Velocity", "V", "Particle velocities (m/s)", GH_ParamAccess.list);
            p.AddNumberParameter("Speed", "Sp", "Particle speeds (m/s)", GH_ParamAccess.list);
            p.AddColourParameter("Colour", "Col", "Their colours", GH_ParamAccess.list);
            p.AddCurveParameter("Trails", "Tr", "One line per particle (its last seconds)", GH_ParamAccess.list);
            p.AddNumberParameter("Age", "A", "Particle ages (s)", GH_ParamAccess.list);
            p.AddNumberParameter("Time", "T", "The simulation time (s)", GH_ParamAccess.item);
            p.AddIntegerParameter("Layer", "Ly", "Which field each particle belongs to", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            ReadWidth(DA, 18);
            var goos = new List<FieldGoo>();
            double t = double.NaN, rate = 300, life = double.NaN, speed = 1, noise = 0, trail = 2;
            double response = 0, hold = 1, drag = 1, join = 2;
            bool run = false, reset = false, outside = false;
            int seed = 1;
            string colour = "speed";
            GradientGoo grad = null;
            var forces = new List<ForceGoo>();
            if (!DA.GetDataList(0, goos)) return;
            var fields = goos.Where(g => g?.Value != null).Select(g => g.Value).ToList();
            if (fields.Count == 0) return;
            bool timed = DA.GetData(1, ref t);
            DA.GetData(2, ref run); DA.GetData(3, ref reset); DA.GetData(4, ref rate); DA.GetData(5, ref life);
            DA.GetData(6, ref speed); DA.GetData(7, ref noise); DA.GetData(8, ref trail); DA.GetData(9, ref seed);
            DA.GetDataList(10, forces);
            DA.GetData(11, ref response); DA.GetData(12, ref hold); DA.GetData(13, ref drag); DA.GetData(14, ref outside);
            DA.GetData(15, ref colour);
            DA.GetData(16, ref grad);
            if (Params.Input.Count > 17) DA.GetData(17, ref join);
            var gradient = grad?.Value;
            if (double.IsNaN(life)) life = fields.Max(f => AutoLife(f, speed));

            // the same fields in a new time slice keep their particles; others restart
            string key = string.Join(";", fields.Select(f =>
            {
                string k = $"{f.Source}|{f.Clip}|{f.Group}|{f.Place.Key}|{f.Warp?.Signature}";
                return double.IsNaN(f.TimeStart) ? k + $"|{f.Valid}" : k;
            }));
            if (_sys == null) _sys = new ParticleSystem();
            _sys.Fields = fields;
            _sys.FieldKey = key;
            _sys.Rate = Math.Max(0, rate); _sys.Life = Math.Max(0.1, life); _sys.SpeedScale = speed;
            _sys.Noise = Math.Max(0, noise); _sys.TrailSeconds = Math.Max(0, trail); _sys.Seed = seed;
            _sys.Response = Math.Max(0, response); _sys.Hold = Math.Max(0, Math.Min(1, hold)); _sys.Drag = Math.Max(0, drag);
            _sys.KeepOutside = outside;
            _sys.Join = Math.Max(0, join);
            _sys.Forces.Clear();
            foreach (var fg in forces) if (fg?.Value != null) _sys.Forces.Add(fg.Value);
            _sys.ForceKey = string.Join(";", _sys.Forces.Select(f => f is ForceBase b ? b.Key : f.GetHashCode().ToString()));

            if (timed)
                _sys.StepTo(Math.Max(0, t));
            else
            {
                if (reset) { _clock = 0; _sys.Reset(); }
                if (run)
                {
                    var now = DateTime.Now;
                    if (_lastTick != DateTime.MinValue) _clock += Math.Min((now - _lastTick).TotalSeconds, 0.25);
                    _lastTick = now;
                    OnPingDocument()?.ScheduleSolution(20, d => ExpireSolution(false));
                }
                else _lastTick = DateTime.MinValue;
                _sys.StepTo(_clock);
            }

            string mode = (colour ?? "").Trim().ToLowerInvariant();
            var pts = new List<Point3d>();
            var vel = new List<Vector3d>();
            var spd = new List<double>();
            var col = new List<Color>();
            var trails = new List<Curve>();
            var age = new List<double>();
            var layer = new List<int>();
            foreach (var p in _sys.Alive)
            {
                var f = fields[p.Layer];
                double scale = Math.Max(f.Scale, 1e-9);
                var pt = new Point3d(p.X, p.Y, p.Z);
                double sp = p.Speed / scale;
                Color c;
                if (mode.StartsWith("l")) c = Preview.LayerColour(p.Layer, fields.Count, gradient);
                else if (mode.StartsWith("a")) c = Preview.Colour(p.Age / Math.Max(p.LifeSpan, 1e-9), gradient);
                else c = Preview.SpeedColour(sp, p.Smax, gradient);
                pts.Add(pt);
                vel.Add(new Vector3d(p.Vx, p.Vy, p.Vz));
                spd.Add(sp);
                col.Add(c);
                age.Add(p.Age);
                layer.Add(p.Layer);
                Cloud.Add(pt, c);
                if (p.Trail.Count >= 2)
                {
                    var tp = p.Trail.Select(q => new Point3d(q.X, q.Y, q.Z)).ToList();
                    trails.Add(new PolylineCurve(tp));
                    for (int k = 1; k < tp.Count; k++) Buckets.Add(new Line(tp[k - 1], tp[k]), c);
                    BakeCurves.Add(trails[trails.Count - 1]);
                    BakeColours.Add(c);
                }
            }
            BakePts.AddRange(pts);
            BakePtColours.AddRange(col);
            DA.SetDataList(0, pts);
            DA.SetDataList(1, vel);
            DA.SetDataList(2, spd);
            DA.SetDataList(3, col);
            DA.SetDataList(4, trails);
            DA.SetDataList(5, age);
            DA.SetData(6, _sys.Time);
            DA.SetDataList(7, layer);
            Message = $"{pts.Count} particles" + (fields.Count > 1 ? $", {fields.Count} layers" : "") + $"\n{_sys.Time:0.0} s";
        }

        /// <summary>Long enough to cross the field at its typical speed (4..40 s).</summary>
        static double AutoLife(FieldData f, double speedScale)
        {
            var (x0, y0, x1, y1) = f.Bounds();
            double span = Math.Max(x1 - x0, y1 - y0);
            var sp = new List<double>();
            for (int k = 0; k < f.Count; k++) if (f.Has[k]) sp.Add(f.Speed[k]);
            if (sp.Count == 0) return 10;
            sp.Sort();
            double med = Math.Max(sp[sp.Count / 2] * f.Scale * Math.Abs(speedScale), 0.2);
            return Math.Max(4, Math.Min(40, 1.2 * span / med));
        }
    }
}
