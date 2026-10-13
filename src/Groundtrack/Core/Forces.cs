using System;
using System.Collections.Generic;
using System.Linq;

namespace Groundtrack.Core
{
    /// <summary>Base for the forces: nothing to prepare, no constraint; a label for keys.</summary>
    public abstract class ForceBase : IForce
    {
        public virtual void Prepare(ParticleSystem s) { }
        public abstract void Apply(ParticleSystem s, ParticleSystem.Particle p, ref double ax, ref double ay, ref double az);
        public virtual bool Constrain(ParticleSystem s, ParticleSystem.Particle p) => true;

        /// <summary>Layers this force acts on (empty = all).</summary>
        public int[] Layers = Array.Empty<int>();
        protected bool Acts(ParticleSystem.Particle p) => Layers.Length == 0 || Array.IndexOf(Layers, p.Layer) >= 0;

        /// <summary>A short description of the settings (a change restarts the simulation).</summary>
        public abstract string Key { get; }

        /// <summary>Smooth falloff: 1 at the centre, 0 at radius (no radius: 1 everywhere).</summary>
        protected static double Falloff(double d, double radius)
        {
            if (radius <= 0) return 1;
            if (d >= radius) return 0;
            double t = 1 - (d / radius) * (d / radius);
            return t * t;
        }
    }

    /// <summary>POP Force: a constant acceleration plus smooth turbulence (noise in space and time).</summary>
    public sealed class UniformForce : ForceBase
    {
        public double Fx, Fy, Fz, Noise, Scale = 10, Speed = 0.5;
        public int Seed = 1;
        double[] _dir, _ph, _fr;

        public override string Key => $"force {Fx},{Fy},{Fz},{Noise},{Scale},{Speed},{Seed},{string.Join(",", Layers)}";

        public override void Apply(ParticleSystem s, ParticleSystem.Particle p, ref double ax, ref double ay, ref double az)
        {
            if (!Acts(p)) return;
            ax += Fx; ay += Fy; az += Fz;
            if (Noise <= 0) return;
            if (_dir == null) Init();
            // 3 octaves of travelling waves per axis: smooth, deterministic
            for (int c = 0; c < 3; c++)
            {
                double v = 0;
                for (int o = 0; o < 3; o++)
                {
                    int k = c * 3 + o;
                    double f = _fr[o] / Math.Max(Scale, 1e-6);
                    v += Math.Sin((p.X * _dir[3 * k] + p.Y * _dir[3 * k + 1] + p.Z * _dir[3 * k + 2]) * f
                                  + s.Time * Speed * (o + 1) + _ph[k]) / (o + 1);
                }
                v *= Noise / 1.83;
                if (c == 0) ax += v; else if (c == 1) ay += v; else az += v;
            }
        }

        void Init()
        {
            var r = new Random(Seed);
            _dir = new double[27]; _ph = new double[9]; _fr = new[] { 1.0, 2.1, 4.3 };
            for (int k = 0; k < 9; k++)
            {
                double a = r.NextDouble() * 2 * Math.PI, b = Math.Acos(2 * r.NextDouble() - 1);
                _dir[3 * k] = Math.Sin(b) * Math.Cos(a); _dir[3 * k + 1] = Math.Sin(b) * Math.Sin(a); _dir[3 * k + 2] = Math.Cos(b);
                _ph[k] = r.NextDouble() * 2 * Math.PI;
            }
        }
    }

    /// <summary>POP Axis Force: orbit around an axis (swirl), suck towards it, lift along it.</summary>
    public sealed class AxisForce : ForceBase
    {
        public double Ox, Oy, Oz, Dx, Dy, Dz = 1, Radius = 20, Orbit = 1, Suction, Lift;

        public override string Key => $"axis {Ox},{Oy},{Oz},{Dx},{Dy},{Dz},{Radius},{Orbit},{Suction},{Lift},{string.Join(",", Layers)}";

        public override void Apply(ParticleSystem s, ParticleSystem.Particle p, ref double ax, ref double ay, ref double az)
        {
            if (!Acts(p)) return;
            double n = Math.Sqrt(Dx * Dx + Dy * Dy + Dz * Dz);
            if (n < 1e-12) return;
            double dx = Dx / n, dy = Dy / n, dz = Dz / n;
            double rx = p.X - Ox, ry = p.Y - Oy, rz = p.Z - Oz;
            double along = rx * dx + ry * dy + rz * dz;
            rx -= along * dx; ry -= along * dy; rz -= along * dz;       // radial vector from the axis
            double d = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            double w = Falloff(d, Radius);
            if (w <= 0 || d < 1e-9) return;
            rx /= d; ry /= d; rz /= d;
            double tx = dy * rz - dz * ry, ty = dz * rx - dx * rz, tz = dx * ry - dy * rx;   // tangent
            ax += w * (Orbit * tx - Suction * rx + Lift * dx);
            ay += w * (Orbit * ty - Suction * ry + Lift * dy);
            az += w * (Orbit * tz - Suction * rz + Lift * dz);
        }
    }

    /// <summary>POP Attract: towards points (Strength &gt; 0) or away (&lt; 0), within Radius.</summary>
    public sealed class PointForce : ForceBase
    {
        public List<(double X, double Y, double Z)> Points = new List<(double, double, double)>();
        public double Strength = 1, Radius = 10;
        public bool Plan;     // pull in plan only (keep heights)

        public override string Key => $"points {Points.Count},{Points.Sum(q => q.X + 3 * q.Y + 7 * q.Z):0.###},{Strength},{Radius},{Plan},{string.Join(",", Layers)}";

        public override void Apply(ParticleSystem s, ParticleSystem.Particle p, ref double ax, ref double ay, ref double az)
        {
            if (!Acts(p)) return;
            foreach (var q in Points)
            {
                double dx = q.X - p.X, dy = q.Y - p.Y, dz = Plan ? 0 : q.Z - p.Z;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double w = Falloff(d, Radius);
                if (w <= 0 || d < 1e-6) continue;
                ax += Strength * w * dx / d; ay += Strength * w * dy / d; az += Strength * w * dz / d;
            }
        }
    }

    /// <summary>Curves as a force: Attract (to the closest point), Follow (along the curve's
    /// direction), Repel; within Radius.</summary>
    public sealed class CurveForce : ForceBase
    {
        public enum Kind { Attract, Follow, Repel }
        public Kind Mode = Kind.Follow;
        public double Strength = 1, Radius = 5;
        readonly Segments _segs = new Segments();

        public CurveForce(IEnumerable<IList<(double X, double Y, double Z)>> curves) { _segs.Build(curves); }

        public override string Key => $"curves {_segs.Count},{_segs.Signature:0.###},{Mode},{Strength},{Radius},{string.Join(",", Layers)}";

        public override void Apply(ParticleSystem s, ParticleSystem.Particle p, ref double ax, ref double ay, ref double az)
        {
            if (!Acts(p) || !_segs.Closest(p.X, p.Y, p.Z, Radius, out var c)) return;
            double w = Falloff(c.Dist, Radius) * Strength;
            if (w == 0) return;
            if (Mode == Kind.Follow)
            {
                ax += w * c.Tx; ay += w * c.Ty; az += w * c.Tz;
                return;
            }
            double sign = Mode == Kind.Attract ? 1 : -1;
            if (c.Dist < 1e-6) return;
            ax += sign * w * (c.X - p.X) / c.Dist;
            ay += sign * w * (c.Y - p.Y) / c.Dist;
            az += sign * w * (c.Z - p.Z) / c.Dist;
        }
    }

    /// <summary>A field as a force (POP Advect by Volumes, force mode / wind): particles are
    /// pulled towards that field's velocity where it has data, and (Height pull) towards its
    /// ground - so another layer's movement drags particles over to it.</summary>
    public sealed class FieldForce : ForceBase
    {
        public FieldData Field;
        public double Strength = 1, HeightPull;

        public override string Key => $"field {Field?.Source}|{Field?.Group}|{Field?.Place.Key}|{Strength},{HeightPull},{string.Join(",", Layers)}";

        public override void Apply(ParticleSystem s, ParticleSystem.Particle p, ref double ax, ref double ay, ref double az)
        {
            if (!Acts(p) || Field == null || !Field.SampleWorld(p.X, p.Y, p.Z, out var f)) return;
            double w = Math.Min(1, f.Confidence * 2);
            // towards that field's velocity along its ground (its normal: up for flat fields)
            var (nx, ny, nz) = Field.Place.Normal;
            double dx = f.Vx * s.SpeedScale - p.Vx, dy = f.Vy * s.SpeedScale - p.Vy, dz = f.Vz * s.SpeedScale - p.Vz;
            double dn = dx * nx + dy * ny + dz * nz;
            ax += Strength * w * (dx - dn * nx);
            ay += Strength * w * (dy - dn * ny);
            az += Strength * w * (dz - dn * nz);
            if (HeightPull == 0) return;
            double h = (f.Gx - p.X) * nx + (f.Gy - p.Y) * ny + (f.Z - p.Z) * nz;     // to its ground
            ax += HeightPull * w * h * nx; ay += HeightPull * w * h * ny; az += HeightPull * w * h * nz;
        }
    }

    /// <summary>POP Interact / flocking: neighbours within Radius push apart (Separation), pull
    /// together (Cohesion) and match velocities (Alignment) - between all particles, only the
    /// same layer, or only other layers (Between): layers that attract each other intertwine.</summary>
    public sealed class InteractForce : ForceBase
    {
        public enum Who { All, Same, Others }
        public Who Between = Who.Others;
        public double Radius = 3, Separation = 1, Cohesion = 1, Alignment = 0.5;
        Dictionary<long, List<ParticleSystem.Particle>> _grid;

        public override string Key => $"interact {Between},{Radius},{Separation},{Cohesion},{Alignment},{string.Join(",", Layers)}";

        static long K(long i, long j, long k) => (i * 73856093) ^ (j * 19349663) ^ (k * 83492791);

        public override void Prepare(ParticleSystem s)
        {
            _grid = new Dictionary<long, List<ParticleSystem.Particle>>();
            double c = Math.Max(Radius, 1e-3);
            foreach (var p in s.Alive)
            {
                long key = K((long)Math.Floor(p.X / c), (long)Math.Floor(p.Y / c), (long)Math.Floor(p.Z / c));
                if (!_grid.TryGetValue(key, out var l)) _grid[key] = l = new List<ParticleSystem.Particle>();
                l.Add(p);
            }
        }

        public override void Apply(ParticleSystem s, ParticleSystem.Particle p, ref double ax, ref double ay, ref double az)
        {
            if (!Acts(p) || _grid == null) return;
            double c = Math.Max(Radius, 1e-3), r2 = Radius * Radius;
            long i0 = (long)Math.Floor(p.X / c), j0 = (long)Math.Floor(p.Y / c), k0 = (long)Math.Floor(p.Z / c);
            double sx = 0, sy = 0, sz = 0, cx = 0, cy = 0, cz = 0, vx = 0, vy = 0, vz = 0;
            int n = 0;
            for (long i = i0 - 1; i <= i0 + 1; i++)
                for (long j = j0 - 1; j <= j0 + 1; j++)
                    for (long k = k0 - 1; k <= k0 + 1; k++)
                    {
                        if (!_grid.TryGetValue(K(i, j, k), out var l)) continue;
                        foreach (var q in l)
                        {
                            if (ReferenceEquals(q, p)) continue;
                            if (Between == Who.Same && q.Layer != p.Layer) continue;
                            if (Between == Who.Others && q.Layer == p.Layer) continue;
                            double dx = p.X - q.X, dy = p.Y - q.Y, dz = p.Z - q.Z;
                            double d2 = dx * dx + dy * dy + dz * dz;
                            if (d2 >= r2 || d2 < 1e-12) continue;
                            double d = Math.Sqrt(d2);
                            double w = 1 - d / Radius;
                            sx += dx / d * w; sy += dy / d * w; sz += dz / d * w;
                            cx += q.X; cy += q.Y; cz += q.Z;
                            vx += q.Vx; vy += q.Vy; vz += q.Vz;
                            n++;
                        }
                    }
            if (n == 0) return;
            ax += Separation * sx + Cohesion * (cx / n - p.X) + Alignment * (vx / n - p.Vx);
            ay += Separation * sy + Cohesion * (cy / n - p.Y) + Alignment * (vy / n - p.Vy);
            az += Separation * sz + Cohesion * (cz / n - p.Z) + Alignment * (vz / n - p.Vz);
        }
    }

    /// <summary>Something particles cannot enter: signed distance (negative inside) and the
    /// outward direction at the closest point.</summary>
    public interface IObstacle
    {
        double SignedDistance(double x, double y, double z, out double nx, out double ny, out double nz);
        string Key { get; }
    }

    /// <summary>A plan footprint (closed polygon) between two heights: a building, a block.</summary>
    public sealed class FootprintObstacle : IObstacle
    {
        readonly double[] _x, _y;
        public double ZMin = double.NegativeInfinity, ZMax = double.PositiveInfinity;

        public FootprintObstacle(IList<(double X, double Y)> polygon)
        {
            var pts = polygon.ToList();
            if (pts.Count > 1 && pts[0] == pts[pts.Count - 1]) pts.RemoveAt(pts.Count - 1);
            _x = pts.Select(q => q.X).ToArray();
            _y = pts.Select(q => q.Y).ToArray();
        }

        public string Key => $"footprint {_x.Length},{_x.Sum():0.###},{_y.Sum():0.###},{ZMin},{ZMax}";

        public double SignedDistance(double x, double y, double z, out double nx, out double ny, out double nz)
        {
            nx = ny = nz = 0;
            if (z < ZMin || z > ZMax || _x.Length < 3) return double.PositiveInfinity;
            bool inside = false;
            double best = double.MaxValue, bx = 0, by = 0;
            for (int i = 0, j = _x.Length - 1; i < _x.Length; j = i++)
            {
                if ((_y[i] > y) != (_y[j] > y) && x < (_x[j] - _x[i]) * (y - _y[i]) / (_y[j] - _y[i]) + _x[i])
                    inside = !inside;
                double ex = _x[i] - _x[j], ey = _y[i] - _y[j];
                double t = Math.Max(0, Math.Min(1, ((x - _x[j]) * ex + (y - _y[j]) * ey) / Math.Max(ex * ex + ey * ey, 1e-12)));
                double qx = _x[j] + t * ex, qy = _y[j] + t * ey;
                double d = (x - qx) * (x - qx) + (y - qy) * (y - qy);
                if (d < best) { best = d; bx = qx; by = qy; }
            }
            double dist = Math.Sqrt(best);
            if (dist < 1e-12) { nx = 0; ny = 0; return 0; }
            double ux = (x - bx) / dist, uy = (y - by) / dist;
            if (inside) { nx = -ux; ny = -uy; return -dist; }
            nx = ux; ny = uy;
            return dist;
        }
    }

    /// <summary>POP Collision: particles keep out of obstacles - pushed away within Margin, and
    /// if they get in, put back on the surface with their velocity reflected (Bounce 0..1) or
    /// killed.</summary>
    public sealed class ObstacleForce : ForceBase
    {
        public List<IObstacle> Obstacles = new List<IObstacle>();
        public double Margin = 1.0, Push = 2.0, Bounce = 0.3;
        public bool Kill;

        public override string Key => $"obstacles {string.Join(";", Obstacles.Select(o => o.Key))},{Margin},{Push},{Bounce},{Kill},{string.Join(",", Layers)}";

        public override void Apply(ParticleSystem s, ParticleSystem.Particle p, ref double ax, ref double ay, ref double az)
        {
            if (!Acts(p) || Margin <= 0) return;
            foreach (var o in Obstacles)
            {
                double d = o.SignedDistance(p.X, p.Y, p.Z, out double nx, out double ny, out double nz);
                if (d <= 0 || d >= Margin) continue;
                double w = Push * (1 - d / Margin) * Math.Max(p.Speed, 0.5);
                ax += w * nx; ay += w * ny; az += w * nz;
            }
        }

        public override bool Constrain(ParticleSystem s, ParticleSystem.Particle p)
        {
            if (!Acts(p)) return true;
            foreach (var o in Obstacles)
            {
                double d = o.SignedDistance(p.X, p.Y, p.Z, out double nx, out double ny, out double nz);
                if (d >= 0) continue;
                if (Kill) return false;
                p.X += nx * (-d + 1e-3); p.Y += ny * (-d + 1e-3); p.Z += nz * (-d + 1e-3);
                double vn = p.Vx * nx + p.Vy * ny + p.Vz * nz;
                if (vn < 0)
                {
                    p.Vx -= (1 + Bounce) * vn * nx; p.Vy -= (1 + Bounce) * vn * ny; p.Vz -= (1 + Bounce) * vn * nz;
                    double un = p.Ux * nx + p.Uy * ny + p.Uz * nz;
                    if (un < 0) { p.Ux -= (1 + Bounce) * un * nx; p.Uy -= (1 + Bounce) * un * ny; p.Uz -= (1 + Bounce) * un * nz; }
                }
                if (p.Trail.Count > 0) p.Trail[p.Trail.Count - 1] = (p.X, p.Y, p.Z);
            }
            return true;
        }
    }

    /// <summary>Polyline segments with a spatial hash: closest point and the direction there.</summary>
    public sealed class Segments
    {
        readonly List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1)> _s =
            new List<(double, double, double, double, double, double)>();
        Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();
        double _cell = 1;
        public int Count => _s.Count;
        public double Signature { get; private set; }

        public struct Hit { public double X, Y, Z, Tx, Ty, Tz, Dist; }

        static long K(long i, long j) => (i << 32) ^ (j & 0xffffffffL);

        public void Build(IEnumerable<IList<(double X, double Y, double Z)>> curves)
        {
            _s.Clear();
            double len = 0;
            foreach (var c in curves)
                for (int k = 0; k + 1 < c.Count; k++)
                {
                    var a = c[k];
                    var b = c[k + 1];
                    if (a == b) continue;
                    _s.Add((a.X, a.Y, a.Z, b.X, b.Y, b.Z));
                    len += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                    Signature += a.X * 0.37 + a.Y * 0.61 + b.X * 0.13 + b.Y * 0.29;
                }
            _cell = Math.Max(_s.Count > 0 ? 2 * len / _s.Count : 1, 0.25);
            _grid = new Dictionary<long, List<int>>();
            for (int n = 0; n < _s.Count; n++)
            {
                var g = _s[n];
                long i0 = (long)Math.Floor(Math.Min(g.X0, g.X1) / _cell), i1 = (long)Math.Floor(Math.Max(g.X0, g.X1) / _cell);
                long j0 = (long)Math.Floor(Math.Min(g.Y0, g.Y1) / _cell), j1 = (long)Math.Floor(Math.Max(g.Y0, g.Y1) / _cell);
                for (long i = i0; i <= i1; i++)
                    for (long j = j0; j <= j1; j++)
                    {
                        if (!_grid.TryGetValue(K(i, j), out var l)) _grid[K(i, j)] = l = new List<int>();
                        l.Add(n);
                    }
            }
        }

        /// <summary>The closest point within r (3D distance, or plan distance when plan is set;
        /// searched by plan cells).</summary>
        public bool Closest(double x, double y, double z, double r, out Hit h, bool plan = false)
        {
            h = default;
            if (_s.Count == 0) return false;
            if (r <= 0) r = 1e9;
            double best = r * r;
            bool found = false;
            long rr = (long)Math.Ceiling(Math.Min(r, 1e6) / _cell);
            long ci = (long)Math.Floor(x / _cell), cj = (long)Math.Floor(y / _cell);
            var seen = new HashSet<int>();
            IEnumerable<int> Candidates()
            {
                if (rr > 400) { for (int n = 0; n < _s.Count; n++) yield return n; yield break; }
                for (long i = ci - rr; i <= ci + rr; i++)
                    for (long j = cj - rr; j <= cj + rr; j++)
                        if (_grid.TryGetValue(K(i, j), out var l))
                            foreach (int n in l) yield return n;
            }
            foreach (int n in Candidates())
            {
                if (!seen.Add(n)) continue;
                var g = _s[n];
                double ex = g.X1 - g.X0, ey = g.Y1 - g.Y0, ez = g.Z1 - g.Z0;
                double l2 = ex * ex + ey * ey + ez * ez;
                double p2 = plan ? ex * ex + ey * ey : l2;
                double dz0 = plan ? 0 : (z - g.Z0) * ez;
                double t = Math.Max(0, Math.Min(1, ((x - g.X0) * ex + (y - g.Y0) * ey + dz0) / Math.Max(p2, 1e-12)));
                double qx = g.X0 + t * ex, qy = g.Y0 + t * ey, qz = g.Z0 + t * ez;
                double d2 = (x - qx) * (x - qx) + (y - qy) * (y - qy) + (plan ? 0 : (z - qz) * (z - qz));
                if (d2 >= best) continue;
                best = d2;
                double l = Math.Sqrt(l2);
                h = new Hit { X = qx, Y = qy, Z = qz, Tx = ex / l, Ty = ey / l, Tz = ez / l, Dist = Math.Sqrt(d2) };
                found = true;
            }
            return found;
        }
    }
}
