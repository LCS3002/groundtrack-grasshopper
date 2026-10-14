using System;
using System.Collections.Generic;
using System.Linq;

namespace Groundtrack.Core
{
    /// <summary>Shaping fields into 3D: outlines, sheets from point grids, lofts between stacked
    /// layers, ribbons (bridges), warps, landscape heights, forces baked into the flow.</summary>
    public static class Shape
    {
        // ---- outlines --------------------------------------------------------------------------

        /// <summary>The outlines of a field's cells with data (marching squares half way between
        /// cells with and without data), rounded off (Chaikin), in local coordinates, counter-
        /// clockwise in the grid, longest first.</summary>
        public static List<List<(double X, double Y)>> Outlines(FieldData f, int rounding = 3)
        {
            int nx = f.Nx + 2, ny = f.Ny + 2;
            bool In(int i, int j) => i >= 1 && j >= 1 && i <= f.Nx && j <= f.Ny && f.Has[(j - 1) * f.Nx + (i - 1)];
            // segments between edge midpoints; an edge id: (cell index, 0 = horizontal / 1 = vertical)
            var next = new Dictionary<long, long>();
            long E(int i, int j, int dir) => ((long)j * nx + i) * 2 + dir;     // h: (i,j)-(i+1,j); v: (i,j)-(i,j+1)
            void Seg(long a, long b) { next[a] = b; }
            for (int j = 0; j < ny - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    bool a = In(i, j), b = In(i + 1, j), c = In(i + 1, j + 1), d = In(i, j + 1);
                    int code = (a ? 1 : 0) | (b ? 2 : 0) | (c ? 4 : 0) | (d ? 8 : 0);
                    long bot = E(i, j, 0), right = E(i + 1, j, 1), top = E(i, j + 1, 0), left = E(i, j, 1);
                    switch (code)
                    {
                        case 1: Seg(left, bot); break;
                        case 2: Seg(bot, right); break;
                        case 3: Seg(left, right); break;
                        case 4: Seg(right, top); break;
                        case 5: Seg(left, top); Seg(right, bot); break;
                        case 6: Seg(bot, top); break;
                        case 7: Seg(left, top); break;
                        case 8: Seg(top, left); break;
                        case 9: Seg(top, bot); break;
                        case 10: Seg(top, right); Seg(bot, left); break;
                        case 11: Seg(top, right); break;
                        case 12: Seg(right, left); break;
                        case 13: Seg(right, bot); break;
                        case 14: Seg(bot, left); break;
                    }
                }
            (double X, double Y) Mid(long e)
            {
                long c = e / 2;
                int dir = (int)(e % 2), i = (int)(c % nx), j = (int)(c / nx);
                double gx = i + (dir == 0 ? 0.5 : 0), gy = j + (dir == 1 ? 0.5 : 0);
                return (f.X0 + (gx - 1) * f.Cell, f.Y0 + (gy - 1) * f.Cell);
            }
            var loops = new List<List<(double X, double Y)>>();
            var used = new HashSet<long>();
            foreach (var start in next.Keys)
            {
                if (used.Contains(start)) continue;
                var loop = new List<(double, double)>();
                long e = start;
                while (!used.Contains(e) && next.ContainsKey(e))
                {
                    used.Add(e);
                    loop.Add(Mid(e));
                    e = next[e];
                }
                loop.Reverse();                     // traced with the data on the right: turn round
                if (loop.Count >= 6) loops.Add(Round(loop, rounding));
            }
            return loops.OrderByDescending(Length).ToList();
        }

        static List<(double X, double Y)> Round(List<(double X, double Y)> pts, int times)
        {
            for (int t = 0; t < times; t++)
            {
                var o = new List<(double, double)>(pts.Count * 2);
                for (int k = 0; k < pts.Count; k++)
                {
                    var a = pts[k];
                    var b = pts[(k + 1) % pts.Count];
                    o.Add((0.75 * a.X + 0.25 * b.X, 0.75 * a.Y + 0.25 * b.Y));
                    o.Add((0.25 * a.X + 0.75 * b.X, 0.25 * a.Y + 0.75 * b.Y));
                }
                pts = o;
            }
            return pts;
        }

        static double Length(List<(double X, double Y)> l)
        {
            double s = 0;
            for (int k = 0; k < l.Count; k++)
            {
                var a = l[k];
                var b = l[(k + 1) % l.Count];
                s += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            }
            return s;
        }

        // ---- sheets from point grids ----------------------------------------------------------

        /// <summary>A field on a grid of world points P[i, j] (i along, j across; spacing about
        /// `cell`): world velocity Vel[i, j] laid onto the sheet, density / confidence per point.
        /// The sheet's axes come from the point grid, so the flow runs along its curves.</summary>
        public static FieldData SheetField(double[,,] P, double[,,] vel, double[,] dens, double cell, double smax,
                                           string site, string group)
        {
            int nu = P.GetLength(0), nv = P.GetLength(1);
            var f = FieldData.Empty(nu, nv, cell, 0, 0);
            f.Site = site; f.Clip = site; f.Group = group;
            f.Tops[group] = smax;
            var s = new Sheet(nu * nv);
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int k = j * nu + i;
                    int ia = Math.Max(0, i - 1), ib = Math.Min(nu - 1, i + 1), ja = Math.Max(0, j - 1), jb = Math.Min(nv - 1, j + 1);
                    double su = (ib - ia) * cell, sv = (jb - ja) * cell;
                    double ux = (P[ib, j, 0] - P[ia, j, 0]) / su, uy = (P[ib, j, 1] - P[ia, j, 1]) / su, uz = (P[ib, j, 2] - P[ia, j, 2]) / su;
                    double vx = (P[i, jb, 0] - P[i, ja, 0]) / sv, vy = (P[i, jb, 1] - P[i, ja, 1]) / sv, vz = (P[i, jb, 2] - P[i, ja, 2]) / sv;
                    s.Px[k] = (float)P[i, j, 0]; s.Py[k] = (float)P[i, j, 1]; s.Pz[k] = (float)P[i, j, 2];
                    s.Ux[k] = (float)ux; s.Uy[k] = (float)uy; s.Uz[k] = (float)uz;
                    s.Vx[k] = (float)vx; s.Vy[k] = (float)vy; s.Vz[k] = (float)vz;
                    // the world velocity in the sheet's axes (least squares: along the sheet)
                    double wx = vel[i, j, 0], wy = vel[i, j, 1], wz = vel[i, j, 2];
                    var (a, b) = InAxes(ux, uy, uz, vx, vy, vz, wx, wy, wz);
                    f.Has[k] = true;
                    f.Vx[k] = (float)a; f.Vy[k] = (float)b;
                    double sp = Math.Sqrt(wx * wx + wy * wy + wz * wz);
                    f.Speed[k] = (float)sp;
                    f.Density[k] = (float)dens[i, j];
                    f.Confidence[k] = 1;
                    f.Smax[k] = (float)smax;
                }
            f.Warp = s;
            return f;
        }

        /// <summary>Components (a, b) of w in the axes u, v (least squares).</summary>
        static (double, double) InAxes(double ux, double uy, double uz, double vx, double vy, double vz, double wx, double wy, double wz)
        {
            double aa = ux * ux + uy * uy + uz * uz, ab = ux * vx + uy * vy + uz * vz, bb = vx * vx + vy * vy + vz * vz;
            double au = ux * wx + uy * wy + uz * wz, av = vx * wx + vy * wy + vz * wz;
            double det = aa * bb - ab * ab;
            if (Math.Abs(det) < 1e-18) return (0, 0);
            return ((bb * au - ab * av) / det, (aa * av - ab * au) / det);
        }

        // ---- loft: stacked layers joined at their edges ----------------------------------------

        /// <summary>A rounded fold from field a's edge to field b's edge (both outlines, lined up):
        /// out of a's edge, swinging out (Swing x the gap) and landing on b's edge from outside,
        /// so the two meet in one continuous sheet. The flow on it blends from a's flow at its
        /// edge into b's. Part of the outline: from `start` to `end` (0..1 along a's outline).</summary>
        public static FieldData Loft(FieldData a, FieldData b, double start = 0, double end = 1, double swing = 1)
        {
            var la = Outlines(a);
            var lb = Outlines(b);
            if (la.Count == 0 || lb.Count == 0) return null;
            double cell = Math.Max(Math.Min(a.WorldCell, b.WorldCell), 0.25);
            // both outlines in world, resampled evenly, lined up
            var A = Resample(EdgeFrames(a, la[0]), cell);
            int n = A.Count;
            var B = Resample(EdgeFrames(b, lb[0]), Perimeter(EdgeFrames(b, lb[0])) / n);
            B = LineUp(A, B);
            if (B.Count != n) B = Resample(B, Perimeter(B) / n, n);
            // the part of the outline
            start = Math.Max(0, Math.Min(1, start)); end = Math.Max(start, Math.Min(1, end));
            bool closed = end - start > 0.999;
            int i0 = (int)Math.Round(start * n), i1 = closed ? i0 + n : (int)Math.Round(end * n);
            int nu = Math.Max(2, i1 - i0 + (closed ? 1 : 1));
            var As = new List<EdgePoint>(nu);
            var Bs = new List<EdgePoint>(nu);
            for (int i = 0; i < nu; i++) { As.Add(A[(i0 + i) % n]); Bs.Add(B[(i0 + i) % n]); }
            return FoldBand(As, Bs, null, swing, cell, "loft", a.Group);
        }

        /// <summary>A fold from edge points As (on one field) to their partners Bs (on another):
        /// out of As along their outward direction, swinging out (Swing x the gap) and landing on
        /// Bs from outside, so the two meet in one sheet. strength (0..1, null = 1): how far the
        /// fold reaches over to Bs - below 1 it stops short, a lip that fades (the ends of a fuse).
        /// The flow blends from As' flow into Bs' across it.</summary>
        static FieldData FoldBand(IList<EdgePoint> As, IList<EdgePoint> Bs, IList<double> strength, double swing,
                                  double cell, string site, string group)
        {
            int nu = As.Count;
            if (nu < 2) return null;
            double W(int i) => strength == null ? 1 : Math.Max(0, Math.Min(1, strength[i]));
            double gap = 0;
            for (int i = 0; i < nu; i++) gap += Dist(As[i].P, Bs[i].P) * W(i);
            gap /= nu;
            int nv = Math.Max(3, (int)Math.Ceiling(gap * (1 + 0.6 * swing) / cell) + 1);
            var P = new double[nu, nv, 3];
            var V = new double[nu, nv, 3];
            var D = new double[nu, nv];
            var far = new double[nu][];
            double smax = 0;
            for (int i = 0; i < nu; i++)
            {
                var ea = As[i];
                var eb = Bs[i];
                double w = W(i);
                var pb = Lerp(ea.P, eb.P, w);
                double L = Dist(ea.P, pb) * swing;
                var t0 = Scale(ea.Out, L);                  // out of the first edge
                var t1 = Scale(eb.Out, -L);                 // into the second
                for (int j = 0; j < nv; j++)
                {
                    double t = j / (double)(nv - 1);
                    double h00 = 2 * t * t * t - 3 * t * t + 1, h10 = t * t * t - 2 * t * t + t;
                    double h01 = -2 * t * t * t + 3 * t * t, h11 = t * t * t - t * t;
                    for (int c = 0; c < 3; c++)
                        P[i, j, c] = h00 * ea.P[c] + h10 * t0[c] + h01 * pb[c] + h11 * t1[c];
                    D[i, j] = ea.Density + (eb.Density - ea.Density) * t * w;
                }
                far[i] = Lerp(ea.Vel, eb.Vel, w);
                smax = Math.Max(smax, Math.Max(ea.Smax, eb.Smax));
            }
            // the flow: each edge's velocity in the fold's own axes at that edge, blended across
            for (int i = 0; i < nu; i++)
            {
                var ea = As[i];
                var (ua, va) = Axes(P, i, 0, nu, nv);
                var (ub, vb) = Axes(P, i, nv - 1, nu, nv);
                var (a0, a1) = InAxes(ua[0], ua[1], ua[2], va[0], va[1], va[2], ea.Vel[0], ea.Vel[1], ea.Vel[2]);
                var (b0, b1) = InAxes(ub[0], ub[1], ub[2], vb[0], vb[1], vb[2], far[i][0], far[i][1], far[i][2]);
                for (int j = 0; j < nv; j++)
                {
                    double t = j / (double)(nv - 1);
                    double e = t * t * (3 - 2 * t);
                    var (u, v) = Axes(P, i, j, nu, nv);
                    double c0 = a0 + (b0 - a0) * e, c1 = a1 + (b1 - a1) * e;
                    for (int c = 0; c < 3; c++) V[i, j, c] = c0 * u[c] + c1 * v[c];
                }
            }
            return SheetField(P, V, D, cell, smax > 0 ? smax : 2.5, site, group);
        }

        /// <summary>Two fields fused where their edges come close, like blobs: along the parts of
        /// a's outline within `reach` of b's, a fold to the nearest part of b's edge, fuller the
        /// closer they are and fading out at the ends. `where` (optional): only edge points of a it
        /// accepts (world). One field per fused stretch.</summary>
        public static List<FieldData> Fuse(FieldData a, FieldData b, double reach, double swing, Func<double[], bool> where = null)
        {
            var res = new List<FieldData>();
            var la = Outlines(a);
            var lb = Outlines(b);
            if (la.Count == 0 || lb.Count == 0 || reach <= 0) return res;
            double cell = Math.Max(Math.Min(a.WorldCell, b.WorldCell), 0.25);
            var A = Resample(EdgeFrames(a, la[0]), cell);
            var B = Resample(EdgeFrames(b, lb[0]), cell);
            int n = A.Count, m = B.Count;
            var match = new int[n];
            var dist = new double[n];
            for (int i = 0; i < n; i++)
            {
                double bd = double.MaxValue;
                for (int j = 0; j < m; j++)
                {
                    double d = Dist(A[i].P, B[j].P);
                    if (d < bd) { bd = d; match[i] = j; }
                }
                dist[i] = bd;
            }
            var on = new bool[n];
            for (int i = 0; i < n; i++) on[i] = dist[i] <= reach && (where == null || where(A[i].P));
            // stretches of a's outline that fuse (round the loop)
            var runs = new List<List<int>>();
            int first = Array.IndexOf(on, false);
            if (first < 0) runs.Add(Enumerable.Range(0, n).ToList());
            else
            {
                List<int> cur = null;
                for (int k = 1; k <= n; k++)
                {
                    int i = (first + k) % n;
                    if (on[i]) { if (cur == null) runs.Add(cur = new List<int>()); cur.Add(i); }
                    else cur = null;
                }
            }
            foreach (var run in runs.Where(r => r.Count >= 3))
            {
                bool closed = run.Count == n;
                var As = run.Select(i => A[i]).ToList();
                // partners: the nearest points of b's edge, smoothed along the stretch (no kinks)
                var Bs = new List<EdgePoint>(run.Count);
                var w = new List<double>(run.Count);
                int taper = Math.Max(2, run.Count / 6);
                for (int k = 0; k < run.Count; k++)
                {
                    var acc = new EdgePoint { P = new double[3], Out = new double[3], Vel = new double[3] };
                    int cnt = 0;
                    for (int dk = -2; dk <= 2; dk++)
                    {
                        int kk = k + dk;
                        if (closed) kk = (kk % run.Count + run.Count) % run.Count;
                        else if (kk < 0 || kk >= run.Count) continue;
                        var e = B[match[run[kk]]];
                        for (int c = 0; c < 3; c++) { acc.P[c] += e.P[c]; acc.Out[c] += e.Out[c]; acc.Vel[c] += e.Vel[c]; }
                        acc.Density += e.Density; acc.Smax = Math.Max(acc.Smax, e.Smax);
                        cnt++;
                    }
                    acc.P = Scale(acc.P, 1.0 / cnt); acc.Out = Unit(acc.Out); acc.Vel = Scale(acc.Vel, 1.0 / cnt);
                    acc.Density /= cnt;
                    Bs.Add(acc);
                    double near = Smooth01((reach - dist[run[k]]) / (0.4 * reach));
                    double ends = closed ? 1 : Smooth01(Math.Min(k, run.Count - 1 - k) / (double)taper);
                    w.Add(near * ends);
                }
                var band = FoldBand(As, Bs, w, swing, cell, "fuse", a.Group);
                if (band != null) res.Add(band);
            }
            return res;
        }

        static double Smooth01(double x)
        {
            x = Math.Max(0, Math.Min(1, x));
            return x * x * (3 - 2 * x);
        }

        // ---- sculpt: terrain shaping in areas ------------------------------------------------------

        /// <summary>Ground raised / lowered by `height` x weight per cell (0..1, e.g. 1 inside an
        /// area fading to 0 at its edge); downhill (0..1): the flow turns down the new slopes.</summary>
        public static FieldData Sculpt(FieldData f, double[] weight, double height, double downhill)
        {
            var dz = new double[f.Count];
            for (int k = 0; k < f.Count; k++) dz[k] = f.Has[k] ? height * weight[k] : 0;
            var o = Lift(f, dz);
            if (downhill <= 0) return o;
            double cw = f.WorldCell;
            for (int j = 0; j < f.Ny; j++)
                for (int i = 0; i < f.Nx; i++)
                {
                    int k = j * f.Nx + i;
                    if (!f.Has[k]) continue;
                    double Dz(int ii, int jj) => ii >= 0 && jj >= 0 && ii < f.Nx && jj < f.Ny && f.Has[jj * f.Nx + ii] ? dz[jj * f.Nx + ii] : dz[k];
                    double gx = (Dz(i + 1, j) - Dz(i - 1, j)) / (2 * cw), gy = (Dz(i, j + 1) - Dz(i, j - 1)) / (2 * cw);
                    double slope = Math.Sqrt(gx * gx + gy * gy);
                    if (slope < 1e-6) continue;
                    double vx = o.Vx[k], vy = o.Vy[k], sp = Math.Sqrt(vx * vx + vy * vy);
                    double t = Math.Min(1, downhill * Math.Min(1, slope * 2));
                    double ux = sp > 1e-9 ? vx / sp : 0, uy = sp > 1e-9 ? vy / sp : 0;
                    double nx = (1 - t) * ux - t * gx / slope, ny = (1 - t) * uy - t * gy / slope;
                    double nl = Math.Sqrt(nx * nx + ny * ny);
                    if (nl < 1e-9) continue;
                    double s2 = Math.Max(sp, 0.3 * downhill);
                    o.Vx[k] = (float)(nx / nl * s2); o.Vy[k] = (float)(ny / nl * s2);
                }
            return o;
        }

        /// <summary>Unit-ish axes of a point grid at (i, j): per grid step / cell (as the sheet stores them).</summary>
        static (double[] U, double[] V) Axes(double[,,] P, int i, int j, int nu, int nv)
        {
            int ia = Math.Max(0, i - 1), ib = Math.Min(nu - 1, i + 1), ja = Math.Max(0, j - 1), jb = Math.Min(nv - 1, j + 1);
            var u = new double[3];
            var v = new double[3];
            for (int c = 0; c < 3; c++)
            {
                u[c] = (P[ib, j, c] - P[ia, j, c]) / Math.Max(ib - ia, 1);
                v[c] = (P[i, jb, c] - P[i, ja, c]) / Math.Max(jb - ja, 1);
            }
            return (u, v);
        }

        /// <summary>A point of a field's outline in world: position, outward direction along the
        /// field's ground, the flow just inside, density there.</summary>
        public sealed class EdgePoint
        {
            public double[] P, Out, Vel;
            public double Density, Smax;
        }

        static List<EdgePoint> EdgeFrames(FieldData f, List<(double X, double Y)> loop)
        {
            var o = new List<EdgePoint>(loop.Count);
            for (int k = 0; k < loop.Count; k++)
            {
                var p = loop[k];
                var q = loop[(k + 1) % loop.Count];
                var r = loop[(k + loop.Count - 1) % loop.Count];
                double tx = q.X - r.X, ty = q.Y - r.Y, tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-12) continue;
                double ox = ty / tl, oy = -tx / tl;           // right of travel: outward (counter-clockwise loop)
                var (wx, wy, wz) = f.LocalToWorld(p.X, p.Y, GroundAt(f, p.X - ox * f.Cell, p.Y - oy * f.Cell));
                var (dx, dy, dz) = f.LocalVectorToWorld(p.X, p.Y, ox, oy);
                double dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                var e = new EdgePoint { P = new[] { wx, wy, wz }, Out = new[] { dx / dl, dy / dl, dz / dl }, Vel = new double[3], Smax = 2.5 };
                // the flow a cell inside
                if (f.SampleLocal(p.X - ox * f.Cell, p.Y - oy * f.Cell, out var s))
                {
                    var (vx, vy, vz) = f.LocalVectorToWorld(p.X, p.Y, s.Vx, s.Vy);
                    e.Vel = new[] { vx, vy, vz };
                    e.Density = s.Density;
                    e.Smax = s.Smax * f.Scale;
                }
                o.Add(e);
            }
            return o;
        }

        static double GroundAt(FieldData f, double lx, double ly) => f.SampleLocal(lx, ly, out var s) ? s.Z : 0;

        static double Perimeter(List<EdgePoint> l)
        {
            double s = 0;
            for (int k = 0; k < l.Count; k++) s += Dist(l[k].P, l[(k + 1) % l.Count].P);
            return s;
        }

        /// <summary>Evenly spaced points along a closed outline (every `step` m, or `count` points).</summary>
        static List<EdgePoint> Resample(List<EdgePoint> l, double step, int count = -1)
        {
            double per = Perimeter(l);
            int n = count > 0 ? count : Math.Max(8, (int)Math.Round(per / Math.Max(step, 1e-6)));
            double seg = per / n;
            var o = new List<EdgePoint>(n);
            int k = 0;
            double acc = 0, target = 0;
            for (int m = 0; m < n; m++, target += seg)
            {
                while (true)
                {
                    double d = Dist(l[k].P, l[(k + 1) % l.Count].P);
                    if (acc + d >= target - 1e-12 || k >= l.Count - 1) break;
                    acc += d; k++;
                }
                var a = l[k];
                var b = l[(k + 1) % l.Count];
                double dd = Dist(a.P, b.P);
                double t = dd < 1e-12 ? 0 : Math.Max(0, Math.Min(1, (target - acc) / dd));
                o.Add(new EdgePoint
                {
                    P = Lerp(a.P, b.P, t), Out = Unit(Lerp(a.Out, b.Out, t)), Vel = Lerp(a.Vel, b.Vel, t),
                    Density = a.Density + (b.Density - a.Density) * t, Smax = Math.Max(a.Smax, b.Smax),
                });
            }
            return o;
        }

        /// <summary>Outline b turned and started so its points face a's (same direction around,
        /// least total distance in plan relative to each outline's centre).</summary>
        static List<EdgePoint> LineUp(List<EdgePoint> a, List<EdgePoint> b)
        {
            double Area(List<EdgePoint> l)
            {
                double s = 0;
                for (int k = 0; k < l.Count; k++) { var p = l[k].P; var q = l[(k + 1) % l.Count].P; s += p[0] * q[1] - q[0] * p[1]; }
                return s;
            }
            if (Math.Sign(Area(a)) != Math.Sign(Area(b)))
            {
                b = new List<EdgePoint>(b);
                b.Reverse();
            }
            double[] Centre(List<EdgePoint> l) => new[] { l.Average(e => e.P[0]), l.Average(e => e.P[1]) };
            var ca = Centre(a);
            var cb = Centre(b);
            int n = b.Count, best = 0;
            double bd = double.MaxValue;
            int stepA = Math.Max(1, a.Count / 64);
            for (int s = 0; s < n; s++)
            {
                double d = 0;
                for (int i = 0; i < a.Count; i += stepA)
                {
                    var p = a[i].P;
                    var q = b[(s + (int)((long)i * n / a.Count)) % n].P;
                    double dx = (p[0] - ca[0]) - (q[0] - cb[0]), dy = (p[1] - ca[1]) - (q[1] - cb[1]);
                    d += dx * dx + dy * dy;
                }
                if (d < bd) { bd = d; best = s; }
            }
            var o = new List<EdgePoint>(n);
            for (int i = 0; i < n; i++) o.Add(b[(best + i) % n]);
            return o;
        }

        // ---- bridges: ribbons along curves ------------------------------------------------------

        /// <summary>A ribbon field along a world polyline: `width` wide, lying across `up` (the
        /// ground's normal along it), flowing along it at `speed`.</summary>
        public static FieldData Ribbon(IList<(double X, double Y, double Z)> curve, double width, double speed,
                                       Func<double, double[]> up, double cell = double.NaN, string group = "bridge")
        {
            var pts = curve.Select(q => new[] { q.X, q.Y, q.Z }).ToList();
            double len = 0;
            var acc = new List<double> { 0 };
            for (int k = 1; k < pts.Count; k++) { len += Dist(pts[k - 1], pts[k]); acc.Add(len); }
            if (len < 1e-6) return null;
            if (double.IsNaN(cell) || cell <= 0) cell = Math.Max(Math.Min(width / 6, len / 30), 0.25);
            // cell centres half a cell inside the ribbon's sides: its edge (half a cell out) is `width` wide
            int nu = Math.Max(2, (int)Math.Ceiling(len / cell) + 1), nv = Math.Max(2, (int)Math.Round(width / cell));
            var P = new double[nu, nv, 3];
            var V = new double[nu, nv, 3];
            var D = new double[nu, nv];
            int seg = 0;
            for (int i = 0; i < nu; i++)
            {
                double s = Math.Min(len, i * len / (nu - 1));
                while (seg < pts.Count - 2 && acc[seg + 1] < s) seg++;
                double d = acc[seg + 1] - acc[seg];
                double t = d < 1e-12 ? 0 : (s - acc[seg]) / d;
                var c = Lerp(pts[seg], pts[seg + 1], t);
                var tan = Unit(Sub(pts[seg + 1], pts[seg]));
                var n = up(s / len);
                var side = Unit(Cross(n, tan));
                for (int j = 0; j < nv; j++)
                {
                    double w = -width / 2 + cell / 2 + (width - cell) * j / (nv - 1);
                    for (int k = 0; k < 3; k++)
                    {
                        P[i, j, k] = c[k] + w * side[k];
                        V[i, j, k] = tan[k] * speed;
                    }
                    D[i, j] = 1;
                }
            }
            return SheetField(P, V, D, cell, Math.Max(speed * 1.5, 0.5), "bridge", group);
        }

        /// <summary>A smooth curve from p0 (leaving along t0) to p1 (arriving along t1), tangents
        /// `swing` x the distance long (cubic Hermite).</summary>
        public static List<(double X, double Y, double Z)> Swoop(double[] p0, double[] t0, double[] p1, double[] t1, double swing, int n = 60)
        {
            double L = Dist(p0, p1) * swing;
            var a = Scale(Unit(t0), L);
            var b = Scale(Unit(t1), L);
            var o = new List<(double, double, double)>(n + 1);
            for (int k = 0; k <= n; k++)
            {
                double t = k / (double)n;
                double h00 = 2 * t * t * t - 3 * t * t + 1, h10 = t * t * t - 2 * t * t + t, h01 = -2 * t * t * t + 3 * t * t, h11 = t * t * t - t * t;
                o.Add((h00 * p0[0] + h10 * a[0] + h01 * p1[0] + h11 * b[0],
                       h00 * p0[1] + h10 * a[1] + h01 * p1[1] + h11 * b[1],
                       h00 * p0[2] + h10 * a[2] + h01 * p1[2] + h11 * b[2]));
            }
            return o;
        }

        /// <summary>Automatic bridges from a to b: where a's flow leaves its edge and b's flow comes
        /// in at its edge, the best-matched pairs within reach (at least two widths apart): curves
        /// leaving along a's flow, arriving along b's.</summary>
        public static List<(List<(double X, double Y, double Z)> Curve, double Speed, double[] UpA, double[] UpB)> AutoBridges(
            FieldData a, FieldData b, int count, double width, double reach, double swing)
        {
            var exits = EdgeFlows(a, outward: true);
            var entries = EdgeFlows(b, outward: false);
            var pairs = new List<(double Score, EdgeFlow X, EdgeFlow Y)>();
            foreach (var x in exits.Take(80))
                foreach (var y in entries.Take(80))
                {
                    double d = Dist(x.P, y.P);
                    if (d > reach || d < width) continue;
                    var dir = Unit(Sub(y.P, x.P));
                    double ca = Dot(Unit(x.V), dir), cb = Dot(Unit(y.V), dir);
                    if (ca < -0.2 || cb < -0.2) continue;              // leaving towards b, arriving from a
                    pairs.Add((Math.Sqrt(x.W * y.W) * (1.5 + ca + cb) / (1 + d / Math.Max(reach, 1e-6)), x, y));
                }
            var picked = new List<(EdgeFlow X, EdgeFlow Y)>();
            foreach (var p in pairs.OrderByDescending(q => q.Score))
            {
                if (picked.Count >= count) break;
                if (picked.Any(q => Dist(q.X.P, p.X.P) < 2 * width || Dist(q.Y.P, p.Y.P) < 2 * width)) continue;
                picked.Add((p.X, p.Y));
            }
            return picked.Select(p => (Swoop(p.X.P, p.X.V, p.Y.P, p.Y.V, swing),
                                       0.5 * (Norm(p.X.V) + Norm(p.Y.V)), p.X.N, p.Y.N)).ToList();
        }

        sealed class EdgeFlow { public double[] P, V, N; public double W; }

        /// <summary>Edge cells whose flow leaves the field (outward) or comes into it, strongest first.</summary>
        static List<EdgeFlow> EdgeFlows(FieldData f, bool outward)
        {
            var o = new List<EdgeFlow>();
            var loops = Outlines(f, 1);
            foreach (var loop in loops.Take(3))
                foreach (var e in EdgeFrames(f, loop))
                {
                    double sp = Norm(e.Vel);
                    if (sp < 0.05) continue;
                    double c = Dot(e.Vel, e.Out) / sp;
                    if (outward ? c < 0.4 : c > -0.4) continue;
                    var (nx, ny, nz) = NormalAt(f, e.P);
                    o.Add(new EdgeFlow { P = e.P, V = e.Vel, N = new[] { nx, ny, nz }, W = Math.Max(e.Density, 1e-3) * Math.Abs(c) });
                }
            return o.OrderByDescending(e => e.W).ToList();
        }

        static (double, double, double) NormalAt(FieldData f, double[] p)
            => f.SampleWorld(p[0], p[1], p[2], out var s) ? (s.Nx, s.Ny, s.Nz) : f.Place.Normal;

        // ---- warps --------------------------------------------------------------------------------

        public enum WarpMode { Twist, Bend, Swirl, Wave, Taper, Bulge }

        /// <summary>A point map for Warp Field. o / d: the axis (start, unit direction), len its
        /// length, up the field's up; amount: degrees (twist, bend, swirl), metres (wave, bulge) or
        /// the end's scale (taper); size: wavelength (wave) or radius (swirl, bulge).</summary>
        public static Func<double, double, double, (double X, double Y, double Z)> Warp(
            WarpMode mode, double[] o, double[] d, double len, double[] up, double amount, double size)
        {
            d = Unit(d); up = Unit(up);
            len = Math.Max(len, 1e-6); size = Math.Max(size, 1e-6);
            var side = Unit(Cross(up, d));
            var upd = Cross(d, side);                       // up, made square to the axis
            switch (mode)
            {
                case WarpMode.Twist:
                    return (x, y, z) =>
                    {
                        var r = new[] { x - o[0], y - o[1], z - o[2] };
                        double t = Math.Max(0, Math.Min(1, Dot(r, d) / len));
                        return Add(o, Rotate(r, d, amount * Math.PI / 180 * t));
                    };
                case WarpMode.Bend:
                    {
                        double ang = amount * Math.PI / 180;
                        if (Math.Abs(ang) < 1e-9) return (x, y, z) => (x, y, z);
                        double R = len / ang;
                        return (x, y, z) =>
                        {
                            var r = new[] { x - o[0], y - o[1], z - o[2] };
                            double s = Dot(r, d), h = Dot(r, upd), w = Dot(r, side);
                            double th = s / R;
                            double along = (R - h) * Math.Sin(th), lift = R - (R - h) * Math.Cos(th);
                            return (o[0] + d[0] * along + upd[0] * lift + side[0] * w,
                                    o[1] + d[1] * along + upd[1] * lift + side[1] * w,
                                    o[2] + d[2] * along + upd[2] * lift + side[2] * w);
                        };
                    }
                case WarpMode.Swirl:
                    return (x, y, z) =>
                    {
                        var r = new[] { x - o[0], y - o[1], z - o[2] };
                        double h = Dot(r, up);
                        var flat = Sub(r, Scale(up, h));
                        double dist = Norm(flat);
                        if (dist >= size) return (x, y, z);
                        double f = 1 - dist / size;
                        return Add(o, Rotate(r, up, amount * Math.PI / 180 * f * f));
                    };
                case WarpMode.Wave:
                    return (x, y, z) =>
                    {
                        var r = new[] { x - o[0], y - o[1], z - o[2] };
                        double lift = amount * Math.Sin(2 * Math.PI * Dot(r, d) / size);
                        return (x + up[0] * lift, y + up[1] * lift, z + up[2] * lift);
                    };
                case WarpMode.Taper:
                    return (x, y, z) =>
                    {
                        var r = new[] { x - o[0], y - o[1], z - o[2] };
                        double s = Dot(r, d), t = Math.Max(0, Math.Min(1, s / len));
                        double k = 1 + (amount - 1) * t;
                        var across = Sub(r, Scale(d, s));
                        var q = Add(o, Add(Scale(d, s), Scale(across, k)));
                        return (q[0], q[1], q[2]);
                    };
                default:   // bulge
                    return (x, y, z) =>
                    {
                        var r = new[] { x - o[0], y - o[1], z - o[2] };
                        double h = Dot(r, up);
                        double dist = Norm(Sub(r, Scale(up, h)));
                        if (dist >= size) return (x, y, z);
                        double f = 1 - (dist / size) * (dist / size);
                        double lift = amount * f * f;
                        return (x + up[0] * lift, y + up[1] * lift, z + up[2] * lift);
                    };
            }
        }

        // ---- landscape heights --------------------------------------------------------------------

        /// <summary>Raise / lower each cell's ground by dz (metres, along the ground's up): the
        /// field's heights for flat / tilted fields, the sheet's points for bent ones.</summary>
        public static FieldData Lift(FieldData f, double[] dz)
        {
            var o = f.Clone(deep: true);
            if (f.Warp == null)
            {
                double sz = Math.Abs(f.Place.Sz) < 1e-12 ? 1 : f.Place.Sz;
                for (int k = 0; k < f.Count; k++) if (f.Has[k]) o.Z[k] = (float)(f.Z[k] + dz[k] / sz);
                return o;
            }
            var w = f.Warp;
            var s = new Sheet(f.Count);
            for (int k = 0; k < f.Count; k++)
            {
                var (nx, ny, nz) = f.CellNormal(k);
                double d = f.Has[k] ? dz[k] : 0;
                s.Px[k] = (float)(w.Px[k] + nx * d); s.Py[k] = (float)(w.Py[k] + ny * d); s.Pz[k] = (float)(w.Pz[k] + nz * d);
                s.Ux[k] = w.Ux[k]; s.Uy[k] = w.Uy[k]; s.Uz[k] = w.Uz[k];
                s.Vx[k] = w.Vx[k]; s.Vy[k] = w.Vy[k]; s.Vz[k] = w.Vz[k];
            }
            o.Warp = s;
            return o;
        }

        /// <summary>Landscape heights: the ground softened over `soften` m (rolling instead of
        /// steps), then raised by `relief` m where the field is busiest ("density") or fastest
        /// ("speed"), smoothly. The change per cell (m), for Lift.</summary>
        public static double[] Landscape(FieldData f, double soften, string reliefBy, double relief)
        {
            int n = f.Count;
            var ground = new double[n];
            for (int k = 0; k < n; k++)
            {
                if (!f.Has[k]) continue;
                // the ground's height along its up (world z for flat fields, the sheet along its normal)
                ground[k] = f.Warp == null ? f.Place.Sz * f.Z[k] : 0;
            }
            var dz = new double[n];
            if (soften > 0 && f.Warp == null)
            {
                var sm = Blur(f, ground, soften);
                for (int k = 0; k < n; k++) if (f.Has[k]) dz[k] = sm[k] - ground[k];
            }
            if (relief != 0 && !string.IsNullOrEmpty(reliefBy) && !reliefBy.StartsWith("n", StringComparison.OrdinalIgnoreCase))
            {
                bool bySpeed = reliefBy.StartsWith("s", StringComparison.OrdinalIgnoreCase);
                var v = new double[n];
                for (int k = 0; k < n; k++)
                    if (f.Has[k]) v[k] = bySpeed ? f.Speed[k] : Math.Sqrt(Math.Max(f.Density[k], 0));
                var sm = Blur(f, v, Math.Max(soften, 3 * f.WorldCell));
                double lo = double.MaxValue, hi = double.MinValue;
                for (int k = 0; k < n; k++) if (f.Has[k]) { lo = Math.Min(lo, sm[k]); hi = Math.Max(hi, sm[k]); }
                if (hi > lo)          // quietest ground stays, the busiest / fastest rises by relief
                    for (int k = 0; k < n; k++) if (f.Has[k]) dz[k] += relief * (sm[k] - lo) / (hi - lo);
            }
            return dz;
        }

        /// <summary>Gaussian average of a value over the cells with data (radius in metres).</summary>
        static double[] Blur(FieldData f, double[] val, double radius)
        {
            double sigma = Math.Max(radius / 2 / f.WorldCell, 0.5);
            int r = (int)Math.Ceiling(3 * sigma), nx = f.Nx, ny = f.Ny;
            var kern = new double[2 * r + 1];
            for (int k = -r; k <= r; k++) kern[k + r] = Math.Exp(-0.5 * k * k / (sigma * sigma));
            var w = new double[f.Count];
            var wv = new double[f.Count];
            for (int k = 0; k < f.Count; k++) if (f.Has[k]) { w[k] = 1; wv[k] = val[k]; }
            double[] Pass(double[] a)
            {
                var t = new double[a.Length];
                var res = new double[a.Length];
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        double s = 0;
                        for (int d = -r; d <= r; d++) { int ii = i + d; if (ii >= 0 && ii < nx) s += kern[d + r] * a[j * nx + ii]; }
                        t[j * nx + i] = s;
                    }
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        double s = 0;
                        for (int d = -r; d <= r; d++) { int jj = j + d; if (jj >= 0 && jj < ny) s += kern[d + r] * t[jj * nx + i]; }
                        res[j * nx + i] = s;
                    }
                return res;
            }
            var bw = Pass(w);
            var bv = Pass(wv);
            var o = new double[f.Count];
            for (int k = 0; k < f.Count; k++) o[k] = bw[k] > 1e-12 ? bv[k] / bw[k] : val[k];
            return o;
        }

        // ---- forces into the flow --------------------------------------------------------------

        /// <summary>The flow bent by forces: at every cell the forces' push (as on a particle there
        /// moving with the flow) is added to the velocity for `seconds`, kept along the ground,
        /// and (keepSpeed) the speed kept, so only the direction changes.</summary>
        public static FieldData ApplyForces(FieldData f, IList<IForce> forces, double seconds, bool keepSpeed)
        {
            var o = f.Clone(deep: true);
            var sys = new ParticleSystem(f);
            foreach (var force in forces) force.Prepare(sys);
            var p = new ParticleSystem.Particle();
            for (int k = 0; k < f.Count; k++)
            {
                if (!f.Has[k]) continue;
                var (x, y, z) = f.CellWorld(k);
                var (vx, vy, vz) = f.CellVelocity(k);
                p.X = x; p.Y = y; p.Z = z; p.Vx = vx; p.Vy = vy; p.Vz = vz; p.Layer = 0;
                double ax = 0, ay = 0, az = 0;
                foreach (var force in forces) force.Apply(sys, p, ref ax, ref ay, ref az);
                double wx = vx + ax * seconds, wy = vy + ay * seconds, wz = vz + az * seconds;
                var (nx, ny, nz) = f.CellNormal(k);
                double dn = wx * nx + wy * ny + wz * nz;
                wx -= dn * nx; wy -= dn * ny; wz -= dn * nz;
                if (keepSpeed)
                {
                    double s0 = Math.Sqrt(vx * vx + vy * vy + vz * vz), s1 = Math.Sqrt(wx * wx + wy * wy + wz * wz);
                    if (s1 > 1e-12) { wx *= s0 / s1; wy *= s0 / s1; wz *= s0 / s1; }
                }
                // back into the field's own axes
                var (ux, uy, uz) = f.LocalVectorToWorld(f.X0 + (k % f.Nx) * f.Cell, f.Y0 + (k / f.Nx) * f.Cell, 1, 0);
                var (qx, qy, qz) = f.LocalVectorToWorld(f.X0 + (k % f.Nx) * f.Cell, f.Y0 + (k / f.Nx) * f.Cell, 0, 1);
                var (a, b) = InAxes(ux, uy, uz, qx, qy, qz, wx, wy, wz);
                o.Vx[k] = (float)a; o.Vy[k] = (float)b;
            }
            return o;
        }

        // ---- small vector helpers ----------------------------------------------------------------

        static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static double Norm(double[] a) => Math.Sqrt(Dot(a, a));
        static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        static (double, double, double) Add(double[] a, (double X, double Y, double Z) b) => (a[0] + b.X, a[1] + b.Y, a[2] + b.Z);
        static double[] Scale(double[] a, double s) => new[] { a[0] * s, a[1] * s, a[2] * s };
        static double[] Lerp(double[] a, double[] b, double t) => new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t };
        static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        static double[] Unit(double[] a) { double l = Norm(a); return l < 1e-12 ? new[] { 0.0, 0.0, 1.0 } : Scale(a, 1 / l); }

        /// <summary>v turned by angle about the unit axis k (Rodrigues).</summary>
        static (double X, double Y, double Z) Rotate(double[] v, double[] k, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            var kxv = Cross(k, v);
            double kv = Dot(k, v);
            return (v[0] * c + kxv[0] * s + k[0] * kv * (1 - c),
                    v[1] * c + kxv[1] * s + k[1] * kv * (1 - c),
                    v[2] * c + kxv[2] * s + k[2] * kv * (1 - c));
        }
    }
}
