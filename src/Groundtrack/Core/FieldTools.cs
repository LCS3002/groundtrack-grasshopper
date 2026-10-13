using System;

namespace Groundtrack.Core
{
    /// <summary>Smoothing and edges of fields: the tools that make merged / blended fields read as
    /// one flow instead of patches with seams.</summary>
    public static class FieldTools
    {
        /// <summary>Distance (in cells) from each cell with data to the nearest cell without
        /// (or the grid border); 0 for cells without data. Chamfer 3-4, two passes.</summary>
        public static float[] EdgeDistance(FieldData f)
        {
            int nx = f.Nx, ny = f.Ny;
            var d = new float[f.Count];
            const float Inf = 1e9f;
            for (int k = 0; k < d.Length; k++) d[k] = f.Has[k] ? Inf : 0;
            float At(int i, int j) => i < 0 || j < 0 || i >= nx || j >= ny ? 0 : d[j * nx + i];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    if (d[k] == 0) continue;
                    d[k] = Math.Min(d[k], Math.Min(Math.Min(At(i - 1, j) + 3, At(i, j - 1) + 3),
                                                   Math.Min(At(i - 1, j - 1) + 4, At(i + 1, j - 1) + 4)));
                }
            for (int j = ny - 1; j >= 0; j--)
                for (int i = nx - 1; i >= 0; i--)
                {
                    int k = j * nx + i;
                    if (d[k] == 0) continue;
                    d[k] = Math.Min(d[k], Math.Min(Math.Min(At(i + 1, j) + 3, At(i, j + 1) + 3),
                                                   Math.Min(At(i + 1, j + 1) + 4, At(i - 1, j + 1) + 4)));
                }
            for (int k = 0; k < d.Length; k++) d[k] /= 3f;
            return d;
        }

        /// <summary>A smooth 0..1 weight that rises from a field's edge to 1 at `feather` metres
        /// inside (world units). 1 everywhere when feather &lt;= 0.</summary>
        public static double EdgeWeight(FieldData f, double wx, double wy, double feather)
        {
            if (feather <= 0) return 1;
            var e = f.Edge;
            var (lx, ly) = f.Place.Inverse(wx, wy);
            double fx = (lx - f.X0) / f.Cell, fy = (ly - f.Y0) / f.Cell;
            int i0 = (int)Math.Floor(fx), j0 = (int)Math.Floor(fy);
            double tx = fx - i0, ty = fy - j0, cells = 0;
            for (int dj = 0; dj <= 1; dj++)                   // bilinear: no staircase
                for (int di = 0; di <= 1; di++)
                {
                    int i = i0 + di, j = j0 + dj;
                    double ek = i < 0 || j < 0 || i >= f.Nx || j >= f.Ny ? 0 : e[j * f.Nx + i];
                    cells += (di == 0 ? 1 - tx : tx) * (dj == 0 ? 1 - ty : ty) * ek;
                }
            // half a cell: the edge itself lies half a cell outside the last cell centre
            double d = (cells - 0.5) * f.WorldCell;
            double t = Math.Max(0, Math.Min(1, (d + 0.5 * f.WorldCell) / feather));
            return t * t * (3 - 2 * t);
        }

        /// <summary>Smooth a field over `radius` metres (Gaussian, sigma = radius / 2): velocity
        /// and height averaged weighted by confidence, so busy cells hold their direction; with
        /// fill, cells without data within the radius get the smoothed flow (gaps between fields
        /// close). Same grid and placement.</summary>
        public static FieldData Smooth(FieldData f, double radius, bool fill = false)
        {
            if (f == null || radius <= 0) return f;
            double sigma = Math.Max(radius / 2 / f.WorldCell, 1e-6);
            int r = (int)Math.Ceiling(3 * sigma);
            var kern = new double[2 * r + 1];
            for (int k = -r; k <= r; k++) kern[k + r] = Math.Exp(-0.5 * k * k / (sigma * sigma));
            int n = f.Count;
            var w = new double[n];
            var ch = new double[7][];                     // w*vx, w*vy, w*z, w*smax, w*speed, density, conf
            for (int c = 0; c < ch.Length; c++) ch[c] = new double[n];
            for (int k = 0; k < n; k++)
            {
                if (!f.Has[k]) continue;
                double wk = Math.Max(f.Confidence[k], 0.02);
                w[k] = wk;
                ch[0][k] = wk * f.Vx[k]; ch[1][k] = wk * f.Vy[k]; ch[2][k] = wk * f.Z[k];
                ch[3][k] = wk * f.Smax[k]; ch[4][k] = wk * f.Speed[k];
                ch[5][k] = f.Density[k]; ch[6][k] = f.Confidence[k];
            }
            var ws = Blur(w, f.Nx, f.Ny, kern, r);
            var cs = new double[ch.Length][];
            for (int c = 0; c < ch.Length; c++) cs[c] = Blur(ch[c], f.Nx, f.Ny, kern, r);
            var hasNear = Blur(Array.ConvertAll(f.Has, h => h ? 1.0 : 0.0), f.Nx, f.Ny, kern, r);
            var o = f.Clone(deep: true);
            for (int k = 0; k < n; k++)
            {
                bool keep = f.Has[k] || (fill && hasNear[k] > 0.15 && ws[k] > 1e-9);
                o.Has[k] = keep;
                if (!keep || ws[k] <= 1e-12) { if (!keep) Zero(o, k); continue; }
                o.Vx[k] = (float)(cs[0][k] / ws[k]); o.Vy[k] = (float)(cs[1][k] / ws[k]);
                o.Z[k] = (float)(cs[2][k] / ws[k]); o.Smax[k] = (float)(cs[3][k] / ws[k]);
                o.Speed[k] = (float)(cs[4][k] / ws[k]);
                o.Density[k] = (float)cs[5][k];
                o.Confidence[k] = (float)Math.Min(1, f.Has[k] ? Math.Max(cs[6][k], 0.5 * f.Confidence[k]) : cs[6][k]);
            }
            o.ResetEdge();
            return o;
        }

        /// <summary>Limit how steep the field's ground may get (rise per run, e.g. 1 = 45 deg):
        /// cliffs (e.g. where layers at different heights meet) become ramps, centred on the
        /// cliff; ground that is not too steep stays exactly as it is. The average of the
        /// steepest surface below (min over q of z(q) + s |p - q|) and above (max of z(q) -
        /// s |p - q|), both by chamfer distance passes through the cells with data.</summary>
        public static FieldData LimitSlope(FieldData f, double maxSlope)
        {
            if (f == null || maxSlope <= 0) return f;
            double step = maxSlope * f.Cell;          // local units: Z and the grid scale alike
            var lo = Envelope(f, step, lower: true);
            var hi = Envelope(f, step, lower: false);
            var o = f.Clone(deep: true);
            for (int k = 0; k < f.Count; k++)
                if (f.Has[k]) o.Z[k] = (float)(0.5 * (lo[k] + hi[k]));
            return o;
        }

        static double[] Envelope(FieldData f, double step, bool lower)
        {
            int nx = f.Nx, ny = f.Ny;
            var e = new double[f.Count];
            for (int k = 0; k < e.Length; k++) e[k] = f.Has[k] ? f.Z[k] : double.NaN;
            double s1 = step, s2 = step * Math.Sqrt(2);
            void Relax(int k, int i, int j, double cost)
            {
                if (i < 0 || j < 0 || i >= nx || j >= ny) return;
                double n = e[j * nx + i];
                if (double.IsNaN(n)) return;
                double c = lower ? n + cost : n - cost;
                if (lower ? c < e[k] : c > e[k]) e[k] = c;
            }
            for (int pass = 0; pass < 3; pass++)
            {
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int k = j * nx + i;
                        if (double.IsNaN(e[k])) continue;
                        Relax(k, i - 1, j, s1); Relax(k, i, j - 1, s1); Relax(k, i - 1, j - 1, s2); Relax(k, i + 1, j - 1, s2);
                    }
                for (int j = ny - 1; j >= 0; j--)
                    for (int i = nx - 1; i >= 0; i--)
                    {
                        int k = j * nx + i;
                        if (double.IsNaN(e[k])) continue;
                        Relax(k, i + 1, j, s1); Relax(k, i, j + 1, s1); Relax(k, i + 1, j + 1, s2); Relax(k, i - 1, j + 1, s2);
                    }
            }
            return e;
        }

        static void Zero(FieldData o, int k)
        {
            o.Vx[k] = o.Vy[k] = o.Speed[k] = o.Density[k] = o.Confidence[k] = 0;
        }

        /// <summary>Separable blur (zero outside the grid).</summary>
        static double[] Blur(double[] a, int nx, int ny, double[] kern, int r)
        {
            var tmp = new double[a.Length];
            var res = new double[a.Length];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double s = 0;
                    for (int d = -r; d <= r; d++)
                    {
                        int ii = i + d;
                        if (ii >= 0 && ii < nx) s += kern[d + r] * a[j * nx + ii];
                    }
                    tmp[j * nx + i] = s;
                }
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double s = 0;
                    for (int d = -r; d <= r; d++)
                    {
                        int jj = j + d;
                        if (jj >= 0 && jj < ny) s += kern[d + r] * tmp[jj * nx + i];
                    }
                    res[j * nx + i] = s;
                }
            return res;
        }
    }
}
