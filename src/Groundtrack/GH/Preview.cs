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

        public static Color SpeedColour(double speed, double top)
        {
            var (r, g, b) = Ramp.Speed(speed, top);
            return Color.FromArgb(r, g, b);
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
            public void AddPolyline(IList<Point3d> pts, IList<double> speed, double top)
            {
                for (int k = 1; k < pts.Count; k++)
                {
                    double s = speed == null ? 0 : 0.5 * (speed[k - 1] + speed[k]);
                    Add(new Line(pts[k - 1], pts[k]), Quantise(s / Math.Max(top, 1e-9)));
                }
            }

            public List<(Line[] Lines, Color Colour)> ToList()
                => _b.Values.Select(v => (v.Lines.ToArray(), v.Colour)).ToList();
        }

        public static Color Quantise(double t)
        {
            t = Math.Max(0, Math.Min(1, double.IsNaN(t) ? 0 : t));
            var (r, g, b) = Ramp.At(Math.Round(t * (Buckets - 1)) / (Buckets - 1));
            return Color.FromArgb(r, g, b);
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
