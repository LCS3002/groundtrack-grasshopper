using System;

namespace Groundtrack.Core
{
    /// <summary>A movement field: a regular grid of cells, each with the mean velocity (m/s) of
    /// the people or vehicles that passed it, how much passed (density), how well it is
    /// supported (confidence 0..1), the top of its group's speed scale (smax, for colours) and
    /// the ground height. Grid coordinates are metres around the site's origin (Easting -> +X,
    /// Northing -> +Y, height above the site's ground level -> +Z). Place moves / turns / scales /
    /// tilts the whole field (Grasshopper Transform nodes); velocities turn and scale with it.</summary>
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

        float[] _edge;

        /// <summary>Distance (cells) of each cell to the field's edge (cached; see FieldTools).</summary>
        public float[] Edge => _edge ?? (_edge = FieldTools.EdgeDistance(this));

        /// <summary>Forget the cached edge distance (after changing which cells have data).</summary>
        public void ResetEdge() { _edge = null; }

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
                f._edge = null;
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

        /// <summary>A cell centre in the field's plane (= world for flat fields).</summary>
        public (double X, double Y, double Z) CellPlane(int k)
        {
            int i = k % Nx, j = k / Nx;
            double lx = X0 + i * Cell, ly = Y0 + j * Cell;
            return (Place.A00 * lx + Place.A01 * ly + Place.Tx, Place.A10 * lx + Place.A11 * ly + Place.Ty,
                    Place.Sz * Z[k] + Place.Tz);
        }

        /// <summary>World velocity of a cell (3D: tilted fields flow along their plane).</summary>
        public (double X, double Y, double Z) CellVelocity(int k) => Place.ApplyVector3(Vx[k], Vy[k]);

        /// <summary>Sample at a world position: the flow there and the ground under / over it,
        /// across the field's plane (straight down for flat fields). Vx / Vy / Vz and Gx / Gy / Z
        /// (the ground point) are in world.</summary>
        public bool SampleWorld(double wx, double wy, double wz, out Sample s)
        {
            if (!Place.Tilted) return Sample(wx, wy, out s);
            var (px, py, _) = Place.ToPlane(wx, wy, wz);
            if (!Sample(px, py, out s)) return false;
            (s.Vx, s.Vy, s.Vz) = Place.ToWorld(s.Vx, s.Vy, 0);
            (s.Gx, s.Gy, s.Z) = Place.ToWorld(px, py, s.Z);
            return true;
        }

        /// <summary>Bilinear sample at a position in the field's plane (world plan for flat
        /// fields; velocities and height in the plane too). False outside the field (less than a
        /// quarter of the neighbourhood has data: the field edge is half a cell out).</summary>
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
            double wz = Place.Sz * z / wsum + Place.Tz;
            s = new Sample
            {
                Vx = wvx, Vy = wvy, Z = wz, Gx = wx, Gy = wy, Confidence = conf / wsum, Density = dens / wsum,
                Smax = smax / wsum, Coverage = wsum, MeanSpeed = msp / wsum * Scale,
            };
            return true;
        }

        /// <summary>World box (3D) of the cells with data, their corners at their heights.</summary>
        public (double X0, double Y0, double Z0, double X1, double Y1, double Z1) WorldBox()
        {
            double a = double.MaxValue, b = double.MaxValue, e = double.MaxValue;
            double c = double.MinValue, d = double.MinValue, g = double.MinValue;
            double h = Cell / 2;
            for (int k = 0; k < Has.Length; k++)
            {
                if (!Has[k]) continue;
                int i = k % Nx, j = k / Nx;
                double lx = X0 + i * Cell, ly = Y0 + j * Cell;
                for (int c4 = 0; c4 < 4; c4++)
                {
                    var (x, y, z) = Place.Apply(lx + (c4 % 2 == 0 ? -h : h), ly + (c4 < 2 ? -h : h), Z[k]);
                    a = Math.Min(a, x); b = Math.Min(b, y); e = Math.Min(e, z);
                    c = Math.Max(c, x); d = Math.Max(d, y); g = Math.Max(g, z);
                }
            }
            return (a, b, e, c, d, g);
        }

        /// <summary>Bounding box of the cells with data in the field's plane (= world plan for
        /// flat fields): (x0, y0, x1, y1).</summary>
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
                    double qx = lx + (c4 % 2 == 0 ? -h : h), qy = ly + (c4 < 2 ? -h : h);
                    double x = Place.A00 * qx + Place.A01 * qy + Place.Tx, y = Place.A10 * qx + Place.A11 * qy + Place.Ty;
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
        public double Vx, Vy, Vz, Z, Confidence, Density, Smax, Coverage;
        public double Gx, Gy;      // the ground point (with Z)
        public double MeanSpeed;   // mean of the individual speeds (the csv's speed column)
        public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz);
    }

    /// <summary>Where a field sits: world = R * (A * local + T). The plan part works in the
    /// field's own plane (2x2 A: turn / scale / mirror; z = Sz * z + Tz); R is a rotation that
    /// tilts that plane (none for fields lying flat, the usual case: then world = plan part).
    /// Built from any Grasshopper / Rhino transform: Rotate about X, Rotate 3D, Orient tilt or
    /// stand the field up, and its flow tilts with it.</summary>
    public readonly struct Placement
    {
        public readonly double A00, A01, A10, A11, Tx, Ty, Sz, Tz;
        /// <summary>The tilt (rows of a rotation matrix); only used when Tilted.</summary>
        public readonly double R00, R01, R02, R10, R11, R12, R20, R21, R22;
        public readonly bool Tilted;

        public Placement(double a00, double a01, double a10, double a11, double tx, double ty,
                         double sz, double tz)
        {
            A00 = a00; A01 = a01; A10 = a10; A11 = a11; Tx = tx; Ty = ty; Sz = sz; Tz = tz;
            R00 = 1; R01 = 0; R02 = 0; R10 = 0; R11 = 1; R12 = 0; R20 = 0; R21 = 0; R22 = 1;
            Tilted = false;
        }

        Placement(double a00, double a01, double a10, double a11, double tx, double ty, double sz, double tz,
                  double[,] r)
        {
            A00 = a00; A01 = a01; A10 = a10; A11 = a11; Tx = tx; Ty = ty; Sz = sz; Tz = tz;
            R00 = r[0, 0]; R01 = r[0, 1]; R02 = r[0, 2];
            R10 = r[1, 0]; R11 = r[1, 1]; R12 = r[1, 2];
            R20 = r[2, 0]; R21 = r[2, 1]; R22 = r[2, 2];
            Tilted = true;
        }

        public static Placement Identity => new Placement(1, 0, 0, 1, 0, 0, 1, 0);
        /// <summary>Area scale of the plan part (lengths scale by its square root).</summary>
        public double Det => A00 * A11 - A01 * A10;

        /// <summary>Local (grid) coordinates to world.</summary>
        public (double X, double Y, double Z) Apply(double x, double y, double z)
            => ToWorld(A00 * x + A01 * y + Tx, A10 * x + A11 * y + Ty, Sz * z + Tz);

        /// <summary>A local plan vector in the field's plane (world plan for flat fields).</summary>
        public (double X, double Y) ApplyVector(double x, double y) => (A00 * x + A01 * y, A10 * x + A11 * y);

        /// <summary>A local plan vector in world (3D).</summary>
        public (double X, double Y, double Z) ApplyVector3(double x, double y)
            => ToWorld(A00 * x + A01 * y, A10 * x + A11 * y, 0);

        /// <summary>A position in the field's plane back to local (grid) coordinates.</summary>
        public (double X, double Y) Inverse(double x, double y)
        {
            double d = Det;
            if (Math.Abs(d) < 1e-12) return (double.NaN, double.NaN);
            double px = x - Tx, py = y - Ty;
            return ((A11 * px - A01 * py) / d, (-A10 * px + A00 * py) / d);
        }

        /// <summary>World to the field's plane (undo the tilt; also for vectors).</summary>
        public (double X, double Y, double Z) ToPlane(double x, double y, double z)
            => !Tilted ? (x, y, z) : (R00 * x + R10 * y + R20 * z, R01 * x + R11 * y + R21 * z, R02 * x + R12 * y + R22 * z);

        /// <summary>The field's plane to world (apply the tilt; also for vectors).</summary>
        public (double X, double Y, double Z) ToWorld(double x, double y, double z)
            => !Tilted ? (x, y, z) : (R00 * x + R01 * y + R02 * z, R10 * x + R11 * y + R12 * z, R20 * x + R21 * y + R22 * z);

        /// <summary>The field's up (its ground's normal) in world: +Z for flat fields.</summary>
        public (double X, double Y, double Z) Normal => Tilted ? (R02, R12, R22) : (0.0, 0.0, 1.0);

        /// <summary>The field's plane axes in world: X and Y for flat fields.</summary>
        public (double X, double Y, double Z) AxisU => Tilted ? (R00, R10, R20) : (1.0, 0.0, 0.0);
        public (double X, double Y, double Z) AxisV => Tilted ? (R01, R11, R21) : (0.0, 1.0, 0.0);

        /// <summary>The same tilt (so plane coordinates are shared)?</summary>
        public bool SameTilt(Placement o)
        {
            if (!Tilted && !o.Tilted) return true;
            if (Tilted != o.Tilted) return false;
            return Math.Abs(R00 - o.R00) + Math.Abs(R01 - o.R01) + Math.Abs(R02 - o.R02) + Math.Abs(R10 - o.R10)
                   + Math.Abs(R11 - o.R11) + Math.Abs(R12 - o.R12) + Math.Abs(R20 - o.R20) + Math.Abs(R21 - o.R21)
                   + Math.Abs(R22 - o.R22) < 1e-9;
        }

        /// <summary>A fingerprint (for caches that restart when the field moves).</summary>
        public string Key => FormattableString.Invariant(
            $"{A00:R},{A01:R},{A10:R},{A11:R},{Tx:R},{Ty:R},{Sz:R},{Tz:R}") +
            (Tilted ? FormattableString.Invariant($"|{R00:R},{R01:R},{R02:R},{R10:R},{R11:R},{R12:R},{R20:R},{R21:R},{R22:R}") : "");

        /// <summary>As a 3x4 world matrix (local -> world).</summary>
        public double[,] Matrix()
        {
            var m = new double[3, 4];
            double[,] r = Tilted ? new[,] { { R00, R01, R02 }, { R10, R11, R12 }, { R20, R21, R22 } }
                                 : new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            double[,] a = { { A00, A01, 0, Tx }, { A10, A11, 0, Ty }, { 0, 0, Sz, Tz } };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 4; j++)
                    m[i, j] = r[i, 0] * a[0, j] + r[i, 1] * a[1, j] + r[i, 2] * a[2, j];
            return m;
        }

        /// <summary>From any 3x4 matrix (local -> world). Transforms that keep the field level stay
        /// a plan placement exactly; ones that tilt it get the rotation of its plane (its local X
        /// and Y axes keep their lengths and angles - no squashing).</summary>
        public static Placement FromMatrix(double[,] m)
        {
            double c0x = m[0, 0], c0y = m[1, 0], c0z = m[2, 0];
            double c1x = m[0, 1], c1y = m[1, 1], c1z = m[2, 1];
            double c2x = m[0, 2], c2y = m[1, 2], c2z = m[2, 2];
            double tx = m[0, 3], ty = m[1, 3], tz = m[2, 3];
            double size = Math.Abs(c0x) + Math.Abs(c0y) + Math.Abs(c0z) + Math.Abs(c1x) + Math.Abs(c1y) + Math.Abs(c1z)
                          + Math.Abs(c2x) + Math.Abs(c2y) + Math.Abs(c2z);
            double tol = 1e-9 * Math.Max(1, size);
            bool level = Math.Abs(c0z) <= tol && Math.Abs(c1z) <= tol && Math.Abs(c2x) <= tol && Math.Abs(c2y) <= tol;
            if (level) return new Placement(c0x, c1x, c0y, c1y, tx, ty, c2z, tz);
            // the plane's normal: across the local X and Y axes, on the side local Z went
            double nx = c0y * c1z - c0z * c1y, ny = c0z * c1x - c0x * c1z, nz = c0x * c1y - c0y * c1x;
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-12 * Math.Max(1, size * size))      // collapsed to a line: keep its plan part
                return new Placement(c0x, c1x, c0y, c1y, tx, ty, c2z, tz);
            nx /= nl; ny /= nl; nz /= nl;
            if (c2x * nx + c2y * ny + c2z * nz < 0) { nx = -nx; ny = -ny; nz = -nz; }
            double l0 = Math.Sqrt(c0x * c0x + c0y * c0y + c0z * c0z);
            double ux = c0x / l0, uy = c0y / l0, uz = c0z / l0;
            double vx = ny * uz - nz * uy, vy = nz * ux - nx * uz, vz = nx * uy - ny * ux;
            var r = new[,] { { ux, vx, nx }, { uy, vy, ny }, { uz, vz, nz } };
            return new Placement(
                ux * c0x + uy * c0y + uz * c0z, ux * c1x + uy * c1y + uz * c1z,
                vx * c0x + vy * c0y + vz * c0z, vx * c1x + vy * c1y + vz * c1z,
                ux * tx + uy * ty + uz * tz, vx * tx + vy * ty + vz * tz,
                nx * c2x + ny * c2y + nz * c2z, nx * tx + ny * ty + nz * tz, r);
        }

        /// <summary>Only the tilt (plan part identity): the frame of this field's plane.</summary>
        public Placement TiltOnly()
        {
            if (!Tilted) return Identity;
            return FromMatrix(new[,] { { R00, R01, R02, 0 }, { R10, R11, R12, 0 }, { R20, R21, R22, 0 } });
        }

        /// <summary>A transform given in this field's plane (3x4) as a world transform.</summary>
        public double[,] PlaneToWorld(double[,] p)
        {
            var x = new double[3, 4];
            for (int j = 0; j < 3; j++)
            {
                var (ex, ey, ez) = ToPlane(j == 0 ? 1 : 0, j == 1 ? 1 : 0, j == 2 ? 1 : 0);
                var (wx, wy, wz) = ToWorld(p[0, 0] * ex + p[0, 1] * ey + p[0, 2] * ez,
                                           p[1, 0] * ex + p[1, 1] * ey + p[1, 2] * ez,
                                           p[2, 0] * ex + p[2, 1] * ey + p[2, 2] * ez);
                x[0, j] = wx; x[1, j] = wy; x[2, j] = wz;
            }
            var (tx, ty, tz) = ToWorld(p[0, 3], p[1, 3], p[2, 3]);
            x[0, 3] = tx; x[1, 3] = ty; x[2, 3] = tz;
            return x;
        }

        /// <summary>This placement followed by a world transform (3x4).</summary>
        public Placement Then(double[,] x)
        {
            var a = Matrix();
            var m = new double[3, 4];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 4; j++)
                    m[i, j] = x[i, 0] * a[0, j] + x[i, 1] * a[1, j] + x[i, 2] * a[2, j] + (j == 3 ? x[i, 3] : 0);
            return FromMatrix(m);
        }

        /// <summary>This placement followed by another.</summary>
        public Placement Then(Placement o) => Then(o.Matrix());
    }
}
