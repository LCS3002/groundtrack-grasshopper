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

        public static FieldData Mix(IList<FieldData> fields, IList<double> weights)
        {
            if (fields == null || fields.Count == 0) return null;
            var w = Enumerable.Range(0, fields.Count)
                              .Select(k => k < weights.Count ? Math.Max(0, weights[k]) : 0.0).ToArray();
            if (w.Sum() <= 0) w = Enumerable.Repeat(1.0, fields.Count).ToArray();
            int refIx = Array.IndexOf(w, w.Max());
            var rf = fields[refIx];

            // the grid: the strongest field's lattice (exact at weight 1), over all fields in play
            var pl = rf.Place;
            bool aligned = Math.Abs(pl.A01) < 1e-9 && Math.Abs(pl.A10) < 1e-9 && pl.A00 > 0
                           && Math.Abs(pl.A00 - pl.A11) < 1e-9;
            double cell = rf.WorldCell;
            double ox = aligned ? pl.A00 * rf.X0 + pl.Tx : 0, oy = aligned ? pl.A11 * rf.Y0 + pl.Ty : 0;
            double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
            for (int k = 0; k < fields.Count; k++)
            {
                if (w[k] <= 0 || fields[k].Valid == 0) continue;
                var b = fields[k].Bounds();
                bx0 = Math.Min(bx0, b.X0); by0 = Math.Min(by0, b.Y0);
                bx1 = Math.Max(bx1, b.X1); by1 = Math.Max(by1, b.Y1);
            }
            while ((bx1 - bx0) / cell * ((by1 - by0) / cell) > MaxCells) cell *= 2;
            int i0 = (int)Math.Floor((bx0 - ox) / cell), i1 = (int)Math.Ceiling((bx1 - ox) / cell);
            int j0 = (int)Math.Floor((by0 - oy) / cell), j1 = (int)Math.Ceiling((by1 - oy) / cell);
            var f = FieldData.Empty(i1 - i0 + 1, j1 - j0 + 1, cell, ox + i0 * cell, oy + j0 * cell);
            f.Site = string.Join("+", fields.Select(x => x.Site).Distinct());
            f.Clip = "blend";
            f.Group = string.Join("+", fields.Select(x => x.Group).Distinct());
            f.OriginE = rf.OriginE; f.OriginN = rf.OriginN; f.Z0 = rf.Z0;
            foreach (var fl in fields) foreach (var kv in fl.Tops) f.Tops[kv.Key] = kv.Value;

            for (int j = 0; j < f.Ny; j++)
                for (int i = 0; i < f.Nx; i++)
                {
                    double x = f.X0 + i * cell, y = f.Y0 + j * cell;
                    double W = 0, vx = 0, vy = 0, z = 0, sm = 0, conf = 0, dens = 0, msp = 0;
                    for (int k = 0; k < fields.Count; k++)
                    {
                        if (w[k] <= 0 || !fields[k].Sample(x, y, out var s)) continue;
                        W += w[k]; vx += w[k] * s.Vx; vy += w[k] * s.Vy; z += w[k] * s.Z;
                        sm += w[k] * s.Smax; conf += w[k] * s.Confidence; dens += w[k] * s.Density;
                        msp += w[k] * s.MeanSpeed;
                    }
                    if (W <= 0) continue;
                    int c = j * f.Nx + i;
                    f.Has[c] = true;
                    f.Vx[c] = (float)(vx / W); f.Vy[c] = (float)(vy / W);
                    f.Speed[c] = (float)(msp / W);
                    f.Z[c] = (float)(z / W); f.Smax[c] = (float)(sm / W);
                    f.Confidence[c] = (float)Math.Min(1, conf); f.Density[c] = (float)dens;
                }
            return f;
        }
    }
}
