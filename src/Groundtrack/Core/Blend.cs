using System;
using System.Collections.Generic;
using System.Linq;

namespace Groundtrack.Core
{
    /// <summary>Layer / interpolate fields: each cell's velocity is the weighted average of the
    /// fields that have data there; confidence and density add up with the weights, so a field
    /// fades in and out with its weight. Weight 1 for one field (0 for the rest) gives that field
    /// back exactly.</summary>
    public static class Blend
    {
        public const int MaxCells = 4_000_000;

        /// <summary>Several fields as one (e.g. sites, clips and groups together): the union of
        /// their cells; where they overlap, the busier flow counts more (density-weighted), the
        /// densities add up and the confidence is the best one.</summary>
        public static FieldData Merge(IList<FieldData> fields, double feather = 0, double smooth = 0, bool fill = false,
                                      double maxSlope = 0)
        {
            if (fields == null || fields.Count == 0) return null;
            if (fields.Count == 1 && smooth <= 0 && maxSlope <= 0) return fields[0];
            var m = Mix(fields, Enumerable.Repeat(1.0, fields.Count).ToList(), byDensity: true, feather: feather,
                        smooth: smooth, fill: fill, maxSlope: maxSlope);
            m.Clip = "merge";
            return m;
        }

        /// <summary>feather (m): each field's weight rises smoothly from 0 at its edge to full
        /// `feather` metres inside, so overlaps ramp in instead of switching on (no seams, no
        /// height jumps); smooth (m): Gaussian smoothing of the result; fill: close gaps.</summary>
        public static FieldData Mix(IList<FieldData> fields, IList<double> weights, bool byDensity = false,
                                   double feather = 0, double smooth = 0, bool fill = false, double maxSlope = 0)
        {
            var f = MixRaw(fields, weights, byDensity, feather);
            if (f == null) return null;
            f = FieldTools.LimitSlope(f, maxSlope);      // cliffs between layers -> ramps
            return FieldTools.Smooth(f, smooth, fill);    // round them off
        }

        static FieldData MixRaw(IList<FieldData> fields, IList<double> weights, bool byDensity, double feather)
        {
            if (fields == null || fields.Count == 0) return null;
            var w = Enumerable.Range(0, fields.Count)
                              .Select(k => k < weights.Count ? Math.Max(0, weights[k]) : 0.0).ToArray();
            if (w.Sum() <= 0) w = Enumerable.Repeat(1.0, fields.Count).ToArray();
            int refIx = Array.IndexOf(w, w.Max());
            var rf = fields[refIx];

            // the grid: the strongest field's lattice (exact at weight 1) in its plane, over all
            // fields in play; fields tilted otherwise are sampled across their own planes
            var pl = rf.Place;
            bool aligned = Math.Abs(pl.A01) < 1e-9 && Math.Abs(pl.A10) < 1e-9 && pl.A00 > 0
                           && Math.Abs(pl.A00 - pl.A11) < 1e-9;
            double cell = rf.WorldCell;
            double ox = aligned ? pl.A00 * rf.X0 + pl.Tx : 0, oy = aligned ? pl.A11 * rf.Y0 + pl.Ty : 0;
            double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
            for (int k = 0; k < fields.Count; k++)
            {
                if (w[k] <= 0 || fields[k].Valid == 0) continue;
                var b = PlaneBounds(fields[k], pl);
                bx0 = Math.Min(bx0, b.X0); by0 = Math.Min(by0, b.Y0);
                bx1 = Math.Max(bx1, b.X1); by1 = Math.Max(by1, b.Y1);
            }
            while ((bx1 - bx0) / cell * ((by1 - by0) / cell) > MaxCells) cell *= 2;
            int i0 = (int)Math.Floor((bx0 - ox) / cell), i1 = (int)Math.Ceiling((bx1 - ox) / cell);
            int j0 = (int)Math.Floor((by0 - oy) / cell), j1 = (int)Math.Ceiling((by1 - oy) / cell);
            var f = FieldData.Empty(i1 - i0 + 1, j1 - j0 + 1, cell, ox + i0 * cell, oy + j0 * cell);
            f.Place = pl.TiltOnly();
            f.Site = string.Join("+", fields.Select(x => x.Site).Distinct());
            f.Clip = "blend";
            f.Group = string.Join("+", fields.Select(x => x.Group).Distinct());
            f.OriginE = rf.OriginE; f.OriginN = rf.OriginN; f.Z0 = rf.Z0;
            foreach (var fl in fields) foreach (var kv in fl.Tops) f.Tops[kv.Key] = kv.Value;

            for (int j = 0; j < f.Ny; j++)
                for (int i = 0; i < f.Nx; i++)
                {
                    double x = f.X0 + i * cell, y = f.Y0 + j * cell;
                    double W = 0, vx = 0, vy = 0, z = 0, sm = 0, conf = 0, dens = 0, msp = 0, best = 0;
                    for (int k = 0; k < fields.Count; k++)
                    {
                        if (w[k] <= 0 || !SampleIn(fields[k], pl, x, y, out var s, out double ex, out double ey)) continue;
                        double edge = FieldTools.EdgeWeight(fields[k], ex, ey, feather);
                        if (edge <= 0) continue;
                        // merge: each field counts with how busy it is there (plus a little, so quiet cells count)
                        double wk = (byDensity ? w[k] * (s.Density + 1e-6) * Math.Max(s.Confidence, 1e-3) : w[k]) * edge;
                        W += wk; vx += wk * s.Vx; vy += wk * s.Vy; z += wk * s.Z;
                        sm += wk * s.Smax; msp += wk * s.MeanSpeed;
                        conf += w[k] * edge * s.Confidence; dens += w[k] * edge * s.Density;
                        best = Math.Max(best, edge * s.Confidence);
                    }
                    if (W <= 0) continue;
                    int c = j * f.Nx + i;
                    f.Has[c] = true;
                    f.Vx[c] = (float)(vx / W); f.Vy[c] = (float)(vy / W);
                    f.Speed[c] = (float)(msp / W);
                    f.Z[c] = (float)(z / W); f.Smax[c] = (float)(sm / W);
                    f.Confidence[c] = (float)(byDensity ? best : Math.Min(1, conf)); f.Density[c] = (float)dens;
                }
            return f;
        }

        /// <summary>Sample field g at (x, y) in the plane of frame: directly when g lies in the
        /// same plane orientation, else across g's own plane, with its velocity and ground turned
        /// into frame's plane. (ex, ey): the point in g's plane.</summary>
        static bool SampleIn(FieldData g, Placement frame, double x, double y, out Sample s, out double ex, out double ey)
        {
            ex = x; ey = y;
            if (g.Place.SameTilt(frame)) return g.Sample(x, y, out s);
            var (wx, wy, wz) = frame.ToWorld(x, y, 0);
            var (px, py, _) = g.Place.ToPlane(wx, wy, wz);
            ex = px; ey = py;
            if (!g.Sample(px, py, out s)) return false;
            var (vx, vy, vz) = g.Place.ToWorld(s.Vx, s.Vy, 0);
            var (gx, gy, gz) = g.Place.ToWorld(px, py, s.Z);
            (s.Vx, s.Vy, _) = frame.ToPlane(vx, vy, vz);
            (_, _, s.Z) = frame.ToPlane(gx, gy, gz);
            s.Vz = 0; s.Gx = x; s.Gy = y;
            return true;
        }

        /// <summary>Bounds of g in the plane of frame.</summary>
        static (double X0, double Y0, double X1, double Y1) PlaneBounds(FieldData g, Placement frame)
        {
            var b = g.Bounds();
            if (g.Place.SameTilt(frame)) return b;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var (cx, cy) in new[] { (b.X0, b.Y0), (b.X1, b.Y0), (b.X0, b.Y1), (b.X1, b.Y1) })
            {
                var (wx, wy, wz) = g.Place.ToWorld(cx, cy, 0);
                var (px, py, _) = frame.ToPlane(wx, wy, wz);
                x0 = Math.Min(x0, px); y0 = Math.Min(y0, py); x1 = Math.Max(x1, px); y1 = Math.Max(y1, py);
            }
            return (x0, y0, x1, y1);
        }
    }
}
