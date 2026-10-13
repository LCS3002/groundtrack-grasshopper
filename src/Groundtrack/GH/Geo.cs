using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Groundtrack
{
    internal static class Geo
    {
        /// <summary>A curve as points: polylines as they are, others divided about every `step` m.</summary>
        public static IList<(double X, double Y, double Z)> Poly(Curve c, double step = 0.25)
        {
            var res = new List<(double, double, double)>();
            if (c == null) return res;
            if (c.TryGetPolyline(out Polyline pl))
                foreach (var p in pl) res.Add((p.X, p.Y, p.Z));
            else
            {
                double len = c.GetLength();
                int n = Math.Max(2, Math.Min(5000, (int)Math.Ceiling(len / step)));
                var ts = c.DivideByCount(n, true);
                if (ts != null) foreach (var t in ts) { var p = c.PointAt(t); res.Add((p.X, p.Y, p.Z)); }
            }
            return res;
        }
    }
}
