using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Groundtrack.Core;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Groundtrack
{
    /// <summary>Speed-coloured drawing: lines grouped into colour buckets so thousands of
    /// segments draw in a few calls.</summary>
    public static class Preview
    {
        public const int Buckets = 48;

        public static Color SpeedColour(double speed, double top, Gradient gradient = null)
            => Colour(speed / Math.Max(top, 1e-9), gradient);

        /// <summary>t (0..1) through a gradient (default: the groundtrack speed ramp).</summary>
        public static Color Colour(double t, Gradient gradient = null)
        {
            var (r, g, b) = gradient == null ? Ramp.At(t) : gradient.At(t);
            return Color.FromArgb(r, g, b);
        }

        /// <summary>Distinct colours for layers 0..n-1 (from a gradient if given).</summary>
        public static Color LayerColour(int layer, int n, Gradient gradient = null)
        {
            if (gradient != null) return Colour(n <= 1 ? 0.5 : layer / (double)(n - 1), gradient);
            Color[] c = { Color.FromArgb(40, 130, 255), Color.FromArgb(255, 60, 190), Color.FromArgb(250, 170, 30),
                          Color.FromArgb(40, 200, 120), Color.FromArgb(150, 90, 255), Color.FromArgb(240, 70, 50) };
            return c[((layer % c.Length) + c.Length) % c.Length];
        }

        public static Color HeatColour(double t)
        {
            var (r, g, b) = Ramp.Heat(t);
            return Color.FromArgb(r, g, b);
        }

        /// <summary>Lines collected per colour (quantised to the ramp's buckets).</summary>
        public sealed class LineBuckets
        {
            readonly Dictionary<int, (List<Line> Lines, Color Colour)> _b = new Dictionary<int, (List<Line>, Color)>();

            public void Add(Line l, Color c)
            {
                int key = c.ToArgb();
                if (!_b.TryGetValue(key, out var e)) _b[key] = e = (new List<Line>(), c);
                e.Lines.Add(l);
            }

            /// <summary>A polyline coloured per segment by speed / top speed.</summary>
            public void AddPolyline(IList<Point3d> pts, IList<double> speed, double top, Gradient gradient = null)
            {
                for (int k = 1; k < pts.Count; k++)
                {
                    double s = speed == null ? 0 : 0.5 * (speed[k - 1] + speed[k]);
                    Add(new Line(pts[k - 1], pts[k]), Quantise(s / Math.Max(top, 1e-9), gradient));
                }
            }

            public List<(Line[] Lines, Color Colour)> ToList()
                => _b.Values.Select(v => (v.Lines.ToArray(), v.Colour)).ToList();
        }

        public static Color Quantise(double t, Gradient gradient = null)
        {
            t = Math.Max(0, Math.Min(1, double.IsNaN(t) ? 0 : t));
            return Colour(Math.Round(t * (Buckets - 1)) / (Buckets - 1), gradient);
        }

        public static void Draw(IGH_PreviewArgs args, List<(Line[] Lines, Color Colour)> lines, int thickness)
        {
            if (lines == null) return;
            foreach (var (l, c) in lines) args.Display.DrawLines(l, c, thickness);
        }

        public static BoundingBox Box(IEnumerable<Point3d> pts)
        {
            var b = BoundingBox.Empty;
            foreach (var p in pts) b.Union(p);
            return b;
        }

        /// <summary>Bake curves with their colours (object colour), into one group.</summary>
        public static void BakeCurves(RhinoDoc doc, ObjectAttributes att, List<Guid> ids,
                                      IList<Curve> curves, IList<Color> colours, string name)
        {
            if (curves == null) return;
            var a = att?.Duplicate() ?? new ObjectAttributes();
            int g = doc.Groups.Add(name);
            for (int k = 0; k < curves.Count; k++)
            {
                if (curves[k] == null) continue;
                var ak = a.Duplicate();
                ak.ColorSource = ObjectColorSource.ColorFromObject;
                ak.ObjectColor = k < colours.Count ? colours[k] : Color.Black;
                ak.AddToGroup(g);
                ids.Add(doc.Objects.AddCurve(curves[k], ak));
            }
        }

        public static void BakePoints(RhinoDoc doc, ObjectAttributes att, List<Guid> ids,
                                      IList<Point3d> pts, IList<Color> colours, string name)
        {
            if (pts == null || pts.Count == 0) return;
            var pc = new PointCloud();
            for (int k = 0; k < pts.Count; k++) pc.Add(pts[k], k < colours.Count ? colours[k] : Color.Black);
            var a = att?.Duplicate() ?? new ObjectAttributes();
            a.Name = name;
            ids.Add(doc.Objects.AddPointCloud(pc, a));
        }
    }
}
