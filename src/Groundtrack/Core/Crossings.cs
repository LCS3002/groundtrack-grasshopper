using System;
using System.Collections.Generic;
using System.Linq;

namespace Groundtrack.Core
{
    /// <summary>Where two sets of lines cross (in plan), and a heat map of the crossings.</summary>
    public static class Crossings
    {
        public struct Hit
        {
            public double X, Y, Z, Angle;     // angle between the two lines, 0..90 degrees
            public int A, B;                  // line indices in set A and set B
        }

        public sealed class Heat
        {
            public double X0, Y0, Cell;
            public int Nx, Ny;
            public double[] Value;            // 0..1, smoothed crossings per cell
            public double Max;                // crossings in the busiest cell (smoothed)
        }

        /// <summary>Crossings of the lines in a with the lines in b (b null: a with itself,
        /// different lines only).</summary>
        public static List<Hit> Find(IList<IList<(double X, double Y, double Z)>> a,
                                     IList<IList<(double X, double Y, double Z)>> b = null)
        {
            bool self = b == null;
            if (self) b = a;
            var hits = new List<Hit>();
            var segsB = new List<(int Line, int Seg)>();
            double sumLen = 0;
            int nLen = 0;
            for (int l = 0; l < b.Count; l++)
                for (int s = 0; s + 1 < b[l].Count; s++)
                {
                    segsB.Add((l, s));
                    if (nLen < 5000) { sumLen += Dist(b[l][s], b[l][s + 1]); nLen++; }
                }
            if (segsB.Count == 0) return hits;
            double cell = Math.Max(2 * sumLen / Math.Max(nLen, 1), 1e-6);
            var grid = new Dictionary<long, List<int>>();
            for (int n = 0; n < segsB.Count; n++)
            {
                var (l, s) = segsB[n];
                foreach (long key in Cells(b[l][s], b[l][s + 1], cell))
                {
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(n);
                }
            }
            var seen = new HashSet<long>();
            for (int la = 0; la < a.Count; la++)
                for (int sa = 0; sa + 1 < a[la].Count; sa++)
                {
                    var p0 = a[la][sa];
                    var p1 = a[la][sa + 1];
                    seen.Clear();
                    foreach (long key in Cells(p0, p1, cell))
                    {
                        if (!grid.TryGetValue(key, out var list)) continue;
                        foreach (int n in list)
                        {
                            if (!seen.Add(n)) continue;
                            var (lb, sb) = segsB[n];
                            if (self && lb <= la) continue;          // each pair of lines once
                            var q0 = b[lb][sb];
                            var q1 = b[lb][sb + 1];
                            if (!Intersect(p0, p1, q0, q1, out double t, out double u)) continue;
                            // a crossing exactly at a shared segment end is counted once
                            if (u >= 1 && sb + 2 < b[lb].Count) continue;
                            if (t >= 1 && sa + 2 < a[la].Count) continue;
                            double ax = p1.X - p0.X, ay = p1.Y - p0.Y, bx = q1.X - q0.X, by = q1.Y - q0.Y;
                            double cos = Math.Abs(ax * bx + ay * by) / Math.Max(Math.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by)), 1e-12);
                            hits.Add(new Hit
                            {
                                X = p0.X + t * ax, Y = p0.Y + t * ay, Z = p0.Z + t * (p1.Z - p0.Z),
                                Angle = Math.Acos(Math.Min(1, cos)) * 180 / Math.PI, A = la, B = lb,
                            });
                        }
                    }
                }
            return hits;
        }

        /// <summary>Crossings per cell, smoothed (Gaussian, sigma = one cell) and scaled to 0..1.</summary>
        public static Heat Map(IList<Hit> hits, double cell = double.NaN)
        {
            var h = new Heat();
            if (hits.Count == 0) return h;
            double x0 = hits.Min(p => p.X), x1 = hits.Max(p => p.X), y0 = hits.Min(p => p.Y), y1 = hits.Max(p => p.Y);
            if (double.IsNaN(cell) || cell <= 0) cell = Math.Max(Math.Max(x1 - x0, y1 - y0) / 60, 0.25);
            h.Cell = cell;
            h.X0 = x0 - 3 * cell; h.Y0 = y0 - 3 * cell;
            h.Nx = (int)Math.Ceiling((x1 - x0) / cell) + 7;
            h.Ny = (int)Math.Ceiling((y1 - y0) / cell) + 7;
            var raw = new double[h.Nx * h.Ny];
            foreach (var p in hits)
            {
                int i = (int)((p.X - h.X0) / cell), j = (int)((p.Y - h.Y0) / cell);
                raw[j * h.Nx + i] += 1;
            }
            double[] k = { 0.0545, 0.2442, 0.4026, 0.2442, 0.0545 };
            var tmp = new double[raw.Length];
            for (int j = 0; j < h.Ny; j++)
                for (int i = 0; i < h.Nx; i++)
                {
                    double s = 0;
                    for (int d = -2; d <= 2; d++)
                        if (i + d >= 0 && i + d < h.Nx) s += k[d + 2] * raw[j * h.Nx + i + d];
                    tmp[j * h.Nx + i] = s;
                }
            h.Value = new double[raw.Length];
            for (int j = 0; j < h.Ny; j++)
                for (int i = 0; i < h.Nx; i++)
                {
                    double s = 0;
                    for (int d = -2; d <= 2; d++)
                        if (j + d >= 0 && j + d < h.Ny) s += k[d + 2] * tmp[(j + d) * h.Nx + i];
                    h.Value[j * h.Nx + i] = s;
                }
            h.Max = h.Value.Max();
            if (h.Max > 0)
                for (int n = 0; n < h.Value.Length; n++) h.Value[n] /= h.Max;
            return h;
        }

        static double Dist((double X, double Y, double Z) a, (double X, double Y, double Z) b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        static IEnumerable<long> Cells((double X, double Y, double Z) a, (double X, double Y, double Z) b, double c)
        {
            long i0 = (long)Math.Floor(Math.Min(a.X, b.X) / c), i1 = (long)Math.Floor(Math.Max(a.X, b.X) / c);
            long j0 = (long)Math.Floor(Math.Min(a.Y, b.Y) / c), j1 = (long)Math.Floor(Math.Max(a.Y, b.Y) / c);
            for (long i = i0; i <= i1; i++)
                for (long j = j0; j <= j1; j++)
                    yield return (i << 32) ^ (j & 0xffffffffL);
        }

        /// <summary>Plan segment intersection: p0 + t (p1 - p0) = q0 + u (q1 - q0), t, u in [0, 1].</summary>
        static bool Intersect((double X, double Y, double Z) p0, (double X, double Y, double Z) p1,
                              (double X, double Y, double Z) q0, (double X, double Y, double Z) q1,
                              out double t, out double u)
        {
            t = u = 0;
            double rx = p1.X - p0.X, ry = p1.Y - p0.Y, sx = q1.X - q0.X, sy = q1.Y - q0.Y;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-12) return false;          // parallel: not a crossing
            double qx = q0.X - p0.X, qy = q0.Y - p0.Y;
            t = (qx * sy - qy * sx) / den;
            u = (qx * ry - qy * rx) / den;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }
    }
}
