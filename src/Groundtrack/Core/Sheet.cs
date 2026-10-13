using System;
using System.Collections.Generic;

namespace Groundtrack.Core
{
    /// <summary>A field bent into 3D (Grasshopper's Bend / Twist / Box Morph, Warp Field, Bridge
    /// ribbons): every cell's world position and the world vectors of its local x and y axes. The
    /// flow stays in the grid's own coordinates, so it runs along the curved sheet. Not changed
    /// once made (transforms and morphs make a new one).</summary>
    public sealed class Sheet
    {
        public readonly float[] Px, Py, Pz, Ux, Uy, Uz, Vx, Vy, Vz;
        Dictionary<long, List<int>> _hash;
        double _hc;
        double _sig = double.NaN;

        public Sheet(int n)
        {
            Px = new float[n]; Py = new float[n]; Pz = new float[n];
            Ux = new float[n]; Uy = new float[n]; Uz = new float[n];
            Vx = new float[n]; Vy = new float[n]; Vz = new float[n];
        }

        /// <summary>A fingerprint of the shape (caches restart when it changes).</summary>
        public double Signature
        {
            get
            {
                if (!double.IsNaN(_sig)) return _sig;
                double s = 0;
                for (int k = 0; k < Px.Length; k += 37) s += Px[k] * 0.37 + Py[k] * 0.61 + Pz[k] * 0.83 + Ux[k] * 0.29 + Vz[k] * 0.17;
                return _sig = s;
            }
        }

        /// <summary>The same sheet after a world transform (3x4).</summary>
        public Sheet Transformed(double[,] x)
        {
            var o = new Sheet(Px.Length);
            for (int k = 0; k < Px.Length; k++)
            {
                double px = Px[k], py = Py[k], pz = Pz[k];
                o.Px[k] = (float)(x[0, 0] * px + x[0, 1] * py + x[0, 2] * pz + x[0, 3]);
                o.Py[k] = (float)(x[1, 0] * px + x[1, 1] * py + x[1, 2] * pz + x[1, 3]);
                o.Pz[k] = (float)(x[2, 0] * px + x[2, 1] * py + x[2, 2] * pz + x[2, 3]);
                double ux = Ux[k], uy = Uy[k], uz = Uz[k], vx = Vx[k], vy = Vy[k], vz = Vz[k];
                o.Ux[k] = (float)(x[0, 0] * ux + x[0, 1] * uy + x[0, 2] * uz);
                o.Uy[k] = (float)(x[1, 0] * ux + x[1, 1] * uy + x[1, 2] * uz);
                o.Uz[k] = (float)(x[2, 0] * ux + x[2, 1] * uy + x[2, 2] * uz);
                o.Vx[k] = (float)(x[0, 0] * vx + x[0, 1] * vy + x[0, 2] * vz);
                o.Vy[k] = (float)(x[1, 0] * vx + x[1, 1] * vy + x[1, 2] * vz);
                o.Vz[k] = (float)(x[2, 0] * vx + x[2, 1] * vy + x[2, 2] * vz);
            }
            return o;
        }

        /// <summary>The cell (with data) nearest to a world point, -1 if none within a few cells.</summary>
        internal int Nearest(FieldData f, double x, double y, double z)
        {
            if (_hash == null) Build(f);
            long ci = (long)Math.Floor(x / _hc), cj = (long)Math.Floor(y / _hc), ck = (long)Math.Floor(z / _hc);
            int best = -1;
            double bd = double.MaxValue;
            for (int r = 0; r <= 4; r++)
            {
                if (best >= 0 && (r - 1) * _hc * ((r - 1) * _hc) > bd) break;
                for (long i = ci - r; i <= ci + r; i++)
                    for (long j = cj - r; j <= cj + r; j++)
                        for (long k = ck - r; k <= ck + r; k++)
                        {
                            if (Math.Max(Math.Abs(i - ci), Math.Max(Math.Abs(j - cj), Math.Abs(k - ck))) != r) continue;
                            if (!_hash.TryGetValue(Key(i, j, k), out var l)) continue;
                            foreach (int c in l)
                            {
                                double dx = Px[c] - x, dy = Py[c] - y, dz = Pz[c] - z;
                                double d = dx * dx + dy * dy + dz * dz;
                                if (d < bd) { bd = d; best = c; }
                            }
                        }
            }
            return best;
        }

        static long Key(long i, long j, long k) => (i * 73856093) ^ (j * 19349663) ^ (k * 83492791);

        void Build(FieldData f)
        {
            double sum = 0;
            int n = 0;
            for (int k = 0; k < f.Count; k++)
            {
                if (!f.Has[k]) continue;
                sum += Math.Sqrt(Ux[k] * Ux[k] + Uy[k] * Uy[k] + Uz[k] * Uz[k]) + Math.Sqrt(Vx[k] * Vx[k] + Vy[k] * Vy[k] + Vz[k] * Vz[k]);
                n += 2;
            }
            _hc = Math.Max(n > 0 ? sum / n * f.Cell * 2 : f.Cell, 1e-3);
            var h = new Dictionary<long, List<int>>();
            for (int k = 0; k < f.Count; k++)
            {
                if (!f.Has[k]) continue;
                long key = Key((long)Math.Floor(Px[k] / _hc), (long)Math.Floor(Py[k] / _hc), (long)Math.Floor(Pz[k] / _hc));
                if (!h.TryGetValue(key, out var l)) h[key] = l = new List<int>();
                l.Add(k);
            }
            _hash = h;
        }
    }

    public sealed partial class FieldData
    {
        /// <summary>The 3D sheet of a bent field; null: Place places it (flat or tilted).</summary>
        public Sheet Warp;

        public bool Warped => Warp != null;

        /// <summary>The same field before it was bent (Place only).</summary>
        public FieldData Unwarped()
        {
            var o = Clone();
            o.Warp = null;
            return o;
        }

        /// <summary>This field as a sheet: its placement baked into positions and axes.</summary>
        public Sheet ToSheet()
        {
            if (Warp != null) return Warp;
            var s = new Sheet(Count);
            var (ux, uy, uz) = Place.ApplyVector3(1, 0);
            var (vx, vy, vz) = Place.ApplyVector3(0, 1);
            for (int k = 0; k < Count; k++)
            {
                var (x, y, z) = Place.Apply(X0 + (k % Nx) * Cell, Y0 + (k / Nx) * Cell, Z[k]);
                s.Px[k] = (float)x; s.Py[k] = (float)y; s.Pz[k] = (float)z;
                s.Ux[k] = (float)ux; s.Uy[k] = (float)uy; s.Uz[k] = (float)uz;
                s.Vx[k] = (float)vx; s.Vy[k] = (float)vy; s.Vz[k] = (float)vz;
            }
            return s;
        }

        /// <summary>Bent by a point map (a space morph): cell positions moved, their local axes
        /// carried along (finite differences), so the flow follows the new shape.</summary>
        public FieldData Morphed(Func<double, double, double, (double X, double Y, double Z)> map)
        {
            var src = ToSheet();
            var s = new Sheet(Count);
            double e = 0.05 * Cell;
            for (int k = 0; k < Count; k++)
            {
                if (!Has[k]) continue;
                double px = src.Px[k], py = src.Py[k], pz = src.Pz[k];
                var p = map(px, py, pz);
                var pu = map(px + e * src.Ux[k], py + e * src.Uy[k], pz + e * src.Uz[k]);
                var pv = map(px + e * src.Vx[k], py + e * src.Vy[k], pz + e * src.Vz[k]);
                s.Px[k] = (float)p.X; s.Py[k] = (float)p.Y; s.Pz[k] = (float)p.Z;
                s.Ux[k] = (float)((pu.X - p.X) / e); s.Uy[k] = (float)((pu.Y - p.Y) / e); s.Uz[k] = (float)((pu.Z - p.Z) / e);
                s.Vx[k] = (float)((pv.X - p.X) / e); s.Vy[k] = (float)((pv.Y - p.Y) / e); s.Vz[k] = (float)((pv.Z - p.Z) / e);
            }
            var o = Clone();
            o.Warp = s;
            return o;
        }

        /// <summary>The unit normal of a cell (its ground's up).</summary>
        public (double X, double Y, double Z) CellNormal(int k)
        {
            if (Warp == null) return Place.Normal;
            var w = Warp;
            return Unit(w.Uy[k] * w.Vz[k] - w.Uz[k] * w.Vy[k], w.Uz[k] * w.Vx[k] - w.Ux[k] * w.Vz[k], w.Ux[k] * w.Vy[k] - w.Uy[k] * w.Vx[k]);
        }

        /// <summary>A point given in local (grid / site) coordinates, in world: on the sheet for
        /// bent fields (lz ignored there: the sheet carries the ground's height).</summary>
        public (double X, double Y, double Z) LocalToWorld(double lx, double ly, double lz)
        {
            if (Warp == null) return Place.Apply(lx, ly, lz);
            if (SheetAt(lx, ly, out var g)) return (g.Px, g.Py, g.Pz);
            int i = Math.Max(0, Math.Min(Nx - 1, (int)Math.Round((lx - X0) / Cell)));
            int j = Math.Max(0, Math.Min(Ny - 1, (int)Math.Round((ly - Y0) / Cell)));
            int k = j * Nx + i;
            return (Warp.Px[k], Warp.Py[k], Warp.Pz[k]);
        }

        /// <summary>A local plan vector at a local point, in world.</summary>
        public (double X, double Y, double Z) LocalVectorToWorld(double lx, double ly, double vx, double vy)
        {
            if (Warp == null || !SheetAt(lx, ly, out var g)) return Place.ApplyVector3(vx, vy);
            return (g.Ux * vx + g.Vx * vy, g.Uy * vx + g.Vy * vy, g.Uz * vx + g.Vz * vy);
        }

        struct SheetPoint { public double Px, Py, Pz, Ux, Uy, Uz, Vx, Vy, Vz; }

        /// <summary>Bilinear position and axes of the sheet at a local point. Corners without data
        /// are carried on from the nearest corner with data along its axes, so the sheet runs on
        /// smoothly to (and a little past) the field's edge instead of shrinking there.</summary>
        bool SheetAt(double lx, double ly, out SheetPoint g)
        {
            g = default;
            var w = Warp;
            double fx = (lx - X0) / Cell, fy = (ly - Y0) / Cell;
            int i0 = (int)Math.Floor(fx), j0 = (int)Math.Floor(fy);
            double tx = fx - i0, ty = fy - j0;
            // the corners with data; the best-weighted one carries the others
            int best = -1;
            double bw = -1;
            for (int c = 0; c < 4; c++)
            {
                int i = i0 + (c & 1), j = j0 + (c >> 1);
                if (i < 0 || j < 0 || i >= Nx || j >= Ny || !Has[j * Nx + i]) continue;
                double wt = ((c & 1) == 0 ? 1 - tx : tx) * ((c >> 1) == 0 ? 1 - ty : ty);
                if (wt > bw) { bw = wt; best = c; }
            }
            if (best < 0) return false;
            int bi = i0 + (best & 1), bj = j0 + (best >> 1), bk = bj * Nx + bi;
            for (int c = 0; c < 4; c++)
            {
                int i = i0 + (c & 1), j = j0 + (c >> 1);
                double wt = ((c & 1) == 0 ? 1 - tx : tx) * ((c >> 1) == 0 ? 1 - ty : ty);
                bool own = i >= 0 && j >= 0 && i < Nx && j < Ny && Has[j * Nx + i];
                int k = own ? j * Nx + i : bk;
                double ox = own ? 0 : (i - bi) * Cell, oy = own ? 0 : (j - bj) * Cell;
                g.Px += wt * (w.Px[k] + w.Ux[k] * ox + w.Vx[k] * oy);
                g.Py += wt * (w.Py[k] + w.Uy[k] * ox + w.Vy[k] * oy);
                g.Pz += wt * (w.Pz[k] + w.Uz[k] * ox + w.Vz[k] * oy);
                g.Ux += wt * w.Ux[k]; g.Uy += wt * w.Uy[k]; g.Uz += wt * w.Uz[k];
                g.Vx += wt * w.Vx[k]; g.Vy += wt * w.Vy[k]; g.Vz += wt * w.Vz[k];
            }
            return true;
        }

        /// <summary>Sample a bent field at a world point: the closest point of the sheet (nearest
        /// cell, then a few Gauss-Newton steps on the bilinear sheet), the flow there in world.</summary>
        bool SampleSheet(double x, double y, double z, out Sample s)
        {
            s = default;
            int k0 = Warp.Nearest(this, x, y, z);
            if (k0 < 0) return false;
            double lx = X0 + (k0 % Nx) * Cell, ly = Y0 + (k0 / Nx) * Cell;
            SheetPoint g = default;
            for (int it = 0; it < 5; it++)
            {
                if (!SheetAt(lx, ly, out g)) return false;
                double rx = x - g.Px, ry = y - g.Py, rz = z - g.Pz;
                double a = g.Ux * g.Ux + g.Uy * g.Uy + g.Uz * g.Uz, b = g.Ux * g.Vx + g.Uy * g.Vy + g.Uz * g.Vz;
                double c = g.Vx * g.Vx + g.Vy * g.Vy + g.Vz * g.Vz;
                double du = g.Ux * rx + g.Uy * ry + g.Uz * rz, dv = g.Vx * rx + g.Vy * ry + g.Vz * rz;
                double det = a * c - b * b;
                if (det <= 1e-18) break;
                double sx = (c * du - b * dv) / det, sy = (a * dv - b * du) / det;
                double m = Math.Max(Math.Abs(sx), Math.Abs(sy));
                if (m > Cell) { sx *= Cell / m; sy *= Cell / m; }
                lx += sx; ly += sy;
                if (m < 1e-5 * Cell) break;
            }
            if (!SampleLocal(lx, ly, out s) || !SheetAt(lx, ly, out g)) return false;
            double vx = s.Vx, vy = s.Vy;
            s.Vx = g.Ux * vx + g.Vx * vy; s.Vy = g.Uy * vx + g.Vy * vy; s.Vz = g.Uz * vx + g.Vz * vy;
            s.Gx = g.Px; s.Gy = g.Py; s.Z = g.Pz;
            (s.Nx, s.Ny, s.Nz) = Unit(g.Uy * g.Vz - g.Uz * g.Vy, g.Uz * g.Vx - g.Ux * g.Vz, g.Ux * g.Vy - g.Uy * g.Vx);
            return true;
        }

        static (double, double, double) Unit(double x, double y, double z)
        {
            double l = Math.Sqrt(x * x + y * y + z * z);
            return l < 1e-12 ? (0.0, 0.0, 1.0) : (x / l, y / l, z / l);
        }
    }
}
