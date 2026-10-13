using System;

namespace Groundtrack.Core
{
    /// <summary>A movement field: a regular grid of cells, each with the mean velocity (m/s) of
    /// the people or vehicles that passed it, how much passed (density), how well it is
    /// supported (confidence 0..1), the top of its group's speed scale (smax, for colours) and
    /// the ground height. Grid coordinates are metres around the site's origin (Easting -> +X,
    /// Northing -> +Y, height above the site's ground level -> +Z). Place moves / turns / scales
    /// the whole field in plan (Grasshopper Transform nodes); velocities turn and scale with it.</summary>
    public sealed class FieldData
    {
        public string Site = "", Clip = "", Group = "", Source = "", TracksPath = "";
        public double OriginE, OriginN, Z0;
        public double Cell = 1;
        public int Nx, Ny;
        public double X0, Y0;                       // local centre of cell (0, 0)
        public float[] Vx, Vy, Speed, Density, Confidence, Smax, Z;
        public bool[] Has;
        public double TimeStart = double.NaN, TimeEnd = double.NaN;   // a time-sliced field
        public Placement Place = Placement.Identity;
        public System.Collections.Generic.Dictionary<string, double> Tops =
            new System.Collections.Generic.Dictionary<string, double>();   // group -> top speed

        public int Count => Nx * Ny;

        public static FieldData Empty(int nx, int ny, double cell, double x0, double y0)
        {
            int n = nx * ny;
            return new FieldData
            {
                Nx = nx, Ny = ny, Cell = cell, X0 = x0, Y0 = y0,
                Vx = new float[n], Vy = new float[n], Speed = new float[n], Density = new float[n],
                Confidence = new float[n], Smax = new float[n], Z = new float[n], Has = new bool[n],
            };
        }

        /// <summary>A copy sharing the grid arrays (placement changes only), or a deep copy.</summary>
        public FieldData Clone(bool deep = false)
        {
            var f = (FieldData)MemberwiseClone();
            if (deep)
            {
                f.Vx = (float[])Vx.Clone(); f.Vy = (float[])Vy.Clone(); f.Speed = (float[])Speed.Clone();
                f.Density = (float[])Density.Clone(); f.Confidence = (float[])Confidence.Clone();
                f.Smax = (float[])Smax.Clone(); f.Z = (float[])Z.Clone(); f.Has = (bool[])Has.Clone();
            }
            return f;
        }

        public string Name => string.IsNullOrEmpty(Site) ? "field"
            : $"{Site} / {Clip} / {Group}"
              + (double.IsNaN(TimeStart) ? "" : $" @ {TimeStart:0.#}-{TimeEnd:0.#} s");

        public int Valid
        {
            get { int n = 0; foreach (bool h in Has) if (h) n++; return n; }
        }

        /// <summary>World position of a cell centre.</summary>
        public (double X, double Y, double Z) CellWorld(int k)
        {
            int i = k % Nx, j = k / Nx;
            return Place.Apply(X0 + i * Cell, Y0 + j * Cell, Z[k]);
        }

        /// <summary>World velocity of a cell.</summary>
        public (double X, double Y) CellVelocity(int k) => Place.ApplyVector(Vx[k], Vy[k]);

        /// <summary>Bilinear sample at a world position (plan). False outside the field (less
        /// than a quarter of the neighbourhood has data: the field edge is half a cell out).</summary>
        public bool Sample(double wx, double wy, out Sample s)
        {
            s = default;
            var (lx, ly) = Place.Inverse(wx, wy);
            if (double.IsNaN(lx)) return false;
            double fx = (lx - X0) / Cell, fy = (ly - Y0) / Cell;
            int i0 = (int)Math.Floor(fx), j0 = (int)Math.Floor(fy);
            double tx = fx - i0, ty = fy - j0;
            double wsum = 0, vx = 0, vy = 0, z = 0, conf = 0, dens = 0, smax = 0, msp = 0;
            for (int dj = 0; dj <= 1; dj++)
            {
                int j = j0 + dj;
                if (j < 0 || j >= Ny) continue;
                double wj = dj == 0 ? 1 - ty : ty;
                for (int di = 0; di <= 1; di++)
                {
                    int i = i0 + di;
                    if (i < 0 || i >= Nx) continue;
                    int k = j * Nx + i;
                    if (!Has[k]) continue;
                    double w = (di == 0 ? 1 - tx : tx) * wj;
                    if (w <= 0) continue;
                    wsum += w; vx += w * Vx[k]; vy += w * Vy[k]; z += w * Z[k];
                    conf += w * Confidence[k]; dens += w * Density[k]; smax += w * Smax[k];
                    msp += w * Speed[k];
                }
            }
            if (wsum < 0.25) return false;
            var (wvx, wvy) = Place.ApplyVector(vx / wsum, vy / wsum);
            var (_, _, wz) = Place.Apply(lx, ly, z / wsum);
            s = new Sample
            {
                Vx = wvx, Vy = wvy, Z = wz, Confidence = conf / wsum, Density = dens / wsum,
                Smax = smax / wsum, Coverage = wsum, MeanSpeed = msp / wsum * Scale,
            };
            return true;
        }

        /// <summary>World bounding box (plan) of the cells with data: (x0, y0, x1, y1).</summary>
        public (double X0, double Y0, double X1, double Y1) Bounds()
        {
            double a = double.MaxValue, b = double.MaxValue, c = double.MinValue, d = double.MinValue;
            double h = Cell / 2;
            for (int k = 0; k < Has.Length; k++)
            {
                if (!Has[k]) continue;
                int i = k % Nx, j = k / Nx;
                double lx = X0 + i * Cell, ly = Y0 + j * Cell;
                for (int c4 = 0; c4 < 4; c4++)
                {
                    var (x, y, _) = Place.Apply(lx + (c4 % 2 == 0 ? -h : h), ly + (c4 < 2 ? -h : h), 0);
                    a = Math.Min(a, x); b = Math.Min(b, y); c = Math.Max(c, x); d = Math.Max(d, y);
                }
            }
            return (a, b, c, d);
        }

        /// <summary>The cell size in world units (after the placement's scale).</summary>
        public double WorldCell => Cell * Scale;

        /// <summary>How much the placement scales lengths (and so speeds).</summary>
        public double Scale => Math.Sqrt(Math.Abs(Place.Det));
    }

    public struct Sample
    {
        public double Vx, Vy, Z, Confidence, Density, Smax, Coverage;
        public double MeanSpeed;   // mean of the individual speeds (the csv's speed column)
        public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy);
    }

    /// <summary>Plan placement: world = A * local + T (2x2 A: rotation / scale / mirror),
    /// z = Sz * z + Tz. Built from the plan part of a Grasshopper / Rhino transform.</summary>
    public readonly struct Placement
    {
        public readonly double A00, A01, A10, A11, Tx, Ty, Sz, Tz;

        public Placement(double a00, double a01, double a10, double a11, double tx, double ty,
                         double sz, double tz)
        {
            A00 = a00; A01 = a01; A10 = a10; A11 = a11; Tx = tx; Ty = ty; Sz = sz; Tz = tz;
        }

        public static Placement Identity => new Placement(1, 0, 0, 1, 0, 0, 1, 0);
        public double Det => A00 * A11 - A01 * A10;

        public (double X, double Y, double Z) Apply(double x, double y, double z)
            => (A00 * x + A01 * y + Tx, A10 * x + A11 * y + Ty, Sz * z + Tz);

        public (double X, double Y) ApplyVector(double x, double y) => (A00 * x + A01 * y, A10 * x + A11 * y);

        public (double X, double Y) Inverse(double x, double y)
        {
            double d = Det;
            if (Math.Abs(d) < 1e-12) return (double.NaN, double.NaN);
            double px = x - Tx, py = y - Ty;
            return ((A11 * px - A01 * py) / d, (-A10 * px + A00 * py) / d);
        }

        /// <summary>This placement followed by another.</summary>
        public Placement Then(Placement o) => new Placement(
            o.A00 * A00 + o.A01 * A10, o.A00 * A01 + o.A01 * A11,
            o.A10 * A00 + o.A11 * A10, o.A10 * A01 + o.A11 * A11,
            o.A00 * Tx + o.A01 * Ty + o.Tx, o.A10 * Tx + o.A11 * Ty + o.Ty,
            o.Sz * Sz, o.Sz * Tz + o.Tz);
    }
}
