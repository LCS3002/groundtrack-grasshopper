using System;
using System.Collections.Generic;
using System.Linq;

namespace Groundtrack.Core
{
    /// <summary>A traced flow line: points in world coordinates with the field speed at each.</summary>
    public sealed class Streamline
    {
        public readonly List<double> X = new List<double>(), Y = new List<double>(), Z = new List<double>(), Speed = new List<double>();
        public double Smax = 2.5;
        public int Count => X.Count;

        public double Length
        {
            get
            {
                double l = 0;
                for (int k = 1; k < X.Count; k++) l += Math.Sqrt(Sq(X[k] - X[k - 1]) + Sq(Y[k] - Y[k - 1]));
                return l;
            }
        }

        public double MeanSpeed => Speed.Count > 0 ? Speed.Average() : 0;
        static double Sq(double v) => v * v;
    }

    /// <summary>Evenly spaced streamlines through a field (after Jobard and Lefer): lines start
    /// where most movement was, are traced both ways along the flow, and stop where they come
    /// closer than half the spacing to another line, leave the field, the flow fades, or the
    /// flow turns sharply within a step (an edge between opposite flows: no zigzag across it).</summary>
    public static class Streamlines
    {
        public static List<Streamline> Trace(FieldData f, double spacing, double minLength = double.NaN,
                                             double minConfidence = 0.05, int maxLines = 20000)
        {
            if (f != null && f.Warped)
            {
                // traced in the grid, then laid onto the bent sheet
                var flat = f.Unwarped();
                var bent = TraceInPlane(flat, spacing, minLength, minConfidence, maxLines);
                foreach (var l in bent)
                    for (int p = 0; p < l.Count; p++)
                    {
                        var (lx, ly) = f.Place.Inverse(l.X[p], l.Y[p]);
                        (l.X[p], l.Y[p], l.Z[p]) = f.LocalToWorld(lx, ly, 0);
                    }
                return bent;
            }
            var lines = TraceInPlane(f, spacing, minLength, minConfidence, maxLines);
            if (f != null && f.Place.Tilted)
                foreach (var l in lines)
                    for (int p = 0; p < l.Count; p++)
                        (l.X[p], l.Y[p], l.Z[p]) = f.Place.ToWorld(l.X[p], l.Y[p], l.Z[p]);
            return lines;
        }

        /// <summary>Streamlines in the field's plane (= world for flat fields).</summary>
        static List<Streamline> TraceInPlane(FieldData f, double spacing, double minLength, double minConfidence, int maxLines)
        {
            var lines = new List<Streamline>();
            if (f == null || f.Valid == 0) return lines;
            double cell = f.WorldCell;
            spacing = Math.Max(spacing, cell * 0.25);
            if (double.IsNaN(minLength)) minLength = 3 * spacing;
            double step = Math.Min(cell * 0.5, spacing * 0.3);
            double test = spacing * 0.5;
            var grid = new Hash(spacing);

            // seeds: cell centres, busiest first
            var seeds = Enumerable.Range(0, f.Count).Where(k => f.Has[k] && f.Confidence[k] >= minConfidence)
                                  .OrderByDescending(k => f.Density[k]).ToList();
            // traced in the field's plane (= world for flat fields), tilted into place at the end
            foreach (int k in seeds)
            {
                if (lines.Count >= maxLines) break;
                var (sx, sy, _) = f.CellPlane(k);
                if (grid.Near(sx, sy, spacing, -1)) continue;
                int id = lines.Count;
                var fwd = Walk(f, sx, sy, step, +1, grid, test, minConfidence, id);
                var back = Walk(f, sx, sy, step, -1, grid, test, minConfidence, id);
                var line = new Streamline();
                for (int b = back.Count - 1; b >= 1; b--) Add(line, back[b]);
                foreach (var p in fwd) Add(line, p);
                if (line.Count < 2 || line.Length < minLength) continue;
                line.Smax = SmaxAt(f, line);
                for (int p = 0; p < line.Count; p++) grid.Add(line.X[p], line.Y[p], id);
                lines.Add(line);
            }
            return lines;
        }

        static void Add(Streamline l, (double X, double Y, double Z, double S) p)
        {
            l.X.Add(p.X); l.Y.Add(p.Y); l.Z.Add(p.Z); l.Speed.Add(p.S);
        }

        static double SmaxAt(FieldData f, Streamline l)
        {
            f.Sample(l.X[l.Count / 2], l.Y[l.Count / 2], out var s);
            return s.Smax > 0 ? s.Smax : 2.5;
        }

        /// <summary>Follow the flow (dir +1) or against it (-1) with midpoint steps.</summary>
        const double MaxTurn = 0.5;      // cos 60 degrees: sharper within a step = an edge, stop

        static List<(double, double, double, double)> Walk(FieldData f, double x, double y, double h, int dir,
                                                           Hash grid, double test, double minConf, int id)
        {
            var pts = new List<(double, double, double, double)>();
            if (!f.Sample(x, y, out var s) || s.Speed < 1e-4) return pts;
            pts.Add((x, y, s.Z, s.Speed));
            var own = new Hash(test);
            for (int n = 0; n < 4000; n++)
            {
                double d1x = dir * s.Vx / s.Speed, d1y = dir * s.Vy / s.Speed;
                if (!f.Sample(x + d1x * h / 2, y + d1y * h / 2, out var m) || m.Speed < 1e-4) break;
                double dmx = dir * m.Vx / m.Speed, dmy = dir * m.Vy / m.Speed;
                if (d1x * dmx + d1y * dmy < MaxTurn) break;
                double nx = x + dmx * h, ny = y + dmy * h;
                if (!f.Sample(nx, ny, out var s2) || s2.Speed < 1e-4 || s2.Confidence < minConf) break;
                if ((dir * s2.Vx * dmx + dir * s2.Vy * dmy) / s2.Speed < MaxTurn) break;
                if (grid.Near(nx, ny, test, id)) break;            // too close to another line
                if (n > 8 && own.Near(nx, ny, h * 0.9, -1)) break;  // runs into itself (a loop)
                if (n > 4) own.Add(pts[pts.Count - 4].Item1, pts[pts.Count - 4].Item2, 0);
                x = nx; y = ny; s = s2;
                pts.Add((x, y, s.Z, s.Speed));
            }
            return pts;
        }

        /// <summary>A spatial hash of line points for the spacing test.</summary>
        sealed class Hash
        {
            readonly double _c;
            readonly Dictionary<long, List<(double X, double Y, int Id)>> _d = new Dictionary<long, List<(double, double, int)>>();
            public Hash(double cell) { _c = Math.Max(cell, 1e-6); }
            static long Key(long i, long j) => (i << 32) ^ (j & 0xffffffffL);

            public void Add(double x, double y, int id)
            {
                long key = Key((long)Math.Floor(x / _c), (long)Math.Floor(y / _c));
                if (!_d.TryGetValue(key, out var l)) _d[key] = l = new List<(double, double, int)>();
                l.Add((x, y, id));
            }

            /// <summary>Any point closer than r (of another line than skipId)?</summary>
            public bool Near(double x, double y, double r, int skipId)
            {
                long i0 = (long)Math.Floor((x - r) / _c), i1 = (long)Math.Floor((x + r) / _c);
                long j0 = (long)Math.Floor((y - r) / _c), j1 = (long)Math.Floor((y + r) / _c);
                double r2 = r * r;
                for (long i = i0; i <= i1; i++)
                    for (long j = j0; j <= j1; j++)
                        if (_d.TryGetValue(Key(i, j), out var l))
                            foreach (var p in l)
                                if (p.Id != skipId && (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) < r2) return true;
                return false;
            }
        }
    }
}
