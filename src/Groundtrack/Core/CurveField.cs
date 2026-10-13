using System;
using System.Collections.Generic;
using System.Linq;

namespace Groundtrack.Core
{
    /// <summary>A field drawn with curves: near each curve the flow runs along it (in the
    /// curve's direction) at Speed, fading out over Width. Blend it with measured fields, or
    /// run particles / streamlines through it.</summary>
    public static class CurveField
    {
        public static FieldData Make(IList<IList<(double X, double Y, double Z)>> curves, double speed, double width,
                                     double cell = double.NaN)
        {
            var segs = new Segments();
            segs.Build(curves);
            if (segs.Count == 0) return null;
            var pts = curves.SelectMany(c => c).ToList();
            double x0 = pts.Min(p => p.X) - width, x1 = pts.Max(p => p.X) + width;
            double y0 = pts.Min(p => p.Y) - width, y1 = pts.Max(p => p.Y) + width;
            if (double.IsNaN(cell) || cell <= 0) cell = Math.Max(width / 4, Math.Max(x1 - x0, y1 - y0) / 600);
            int nx = (int)Math.Ceiling((x1 - x0) / cell) + 1, ny = (int)Math.Ceiling((y1 - y0) / cell) + 1;
            var f = FieldData.Empty(nx, ny, cell, x0, y0);
            f.Site = "curves"; f.Clip = "drawn"; f.Group = "drawn";
            f.Tops["drawn"] = Math.Max(speed, 0.1) * 1.5;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double x = x0 + i * cell, y = y0 + j * cell;
                    // plan distance: heights only follow the curve
                    if (!segs.Closest(x, y, 0, width, out var h, plan: true)) continue;
                    double d = h.Dist;
                    if (d >= width) continue;
                    double w = 1 - (d / width) * (d / width);
                    w *= w;
                    double tl = Math.Sqrt(h.Tx * h.Tx + h.Ty * h.Ty);
                    if (tl < 1e-9) continue;
                    int k = j * nx + i;
                    f.Has[k] = true;
                    f.Vx[k] = (float)(h.Tx / tl * speed);
                    f.Vy[k] = (float)(h.Ty / tl * speed);
                    f.Speed[k] = (float)speed;
                    f.Confidence[k] = (float)w;
                    f.Density[k] = (float)w;
                    f.Smax[k] = (float)(Math.Max(speed, 0.1) * 1.5);
                    f.Z[k] = (float)h.Z;
                }
            return f;
        }
    }
}
