using System;
using System.Collections.Generic;

namespace Groundtrack.Core
{
    /// <summary>A force on particles (like a POP force node): an acceleration (m/s^2) per
    /// particle, optionally a clean-up after each move (collisions).</summary>
    public interface IForce
    {
        /// <summary>Once per step, before the particles move (e.g. find neighbours).</summary>
        void Prepare(ParticleSystem s);

        /// <summary>Add this force's acceleration on particle p.</summary>
        void Apply(ParticleSystem s, ParticleSystem.Particle p, ref double ax, ref double ay, ref double az);

        /// <summary>After the move (e.g. push out of obstacles); false kills the particle.</summary>
        bool Constrain(ParticleSystem s, ParticleSystem.Particle p);
    }

    /// <summary>Particles through one or more fields (layers), like a Houdini POP network:
    /// born where people / vehicles were (busier cells more), moved by their layer's field,
    /// pushed by forces, killed when they leave their field, stall or reach their life.
    /// Response 0: they take the field's velocity (POP Advect, velocity); above 0 they have
    /// inertia and reach it within about Response seconds (force). Hold keeps them on their
    /// layer's ground (1 = on it, 0 = free across it: up for flat layers, along the normal for
    /// tilted ones). Join: a particle leaving its field carries on onto another layer whose
    /// ground is there or a short gap ahead (fields meeting edge to edge - a floor, a slope, a
    /// wall - are one surface). Deterministic for a seed: the state at time t is the same however
    /// you get there, so a timeline can scrub.</summary>
    public sealed class ParticleSystem
    {
        public List<FieldData> Fields = new List<FieldData>();
        public double Rate = 200, Life = 20, SpeedScale = 1, Noise = 0, TrailSeconds = 2, Dt = 1 / 30.0;
        public double MinConfidence = 0.0, Response = 0, Drag = 1.0, Hold = 1.0;
        public bool KeepOutside;
        /// <summary>Hand-over distance (m): a particle leaving its field joins another layer whose
        /// ground is within this distance - there, or across a gap ahead of it (it goes straight on
        /// across). 0 = off: it dies, or coasts with KeepOutside.</summary>
        public double Join = 2.0;
        public int Seed = 1;
        public readonly List<IForce> Forces = new List<IForce>();

        /// <summary>Which fields these are (a new time slice of the same fields keeps the
        /// particles; other fields or settings restart).</summary>
        public string FieldKey = "";

        public FieldData Field
        {
            get => Fields.Count > 0 ? Fields[0] : null;
            set { Fields.Clear(); if (value != null) Fields.Add(value); }
        }

        public double Time { get; private set; }
        public readonly List<Particle> Alive = new List<Particle>();
        Random _rnd;
        double[] _acc;
        int _nextId;
        double[][] _cdf;
        int[][] _cells;
        string _used;

        public sealed class Particle
        {
            public int Id, Layer, Joined;
            public double X, Y, Z, Vx, Vy, Vz, Ux, Uy, Uz, Age, LifeSpan, Smax, NoiseX, NoiseY, Stalled;
            /// <summary>Metres gone straight on across a gap towards another layer (0: on a field).</summary>
            public double Bridge;
            public readonly List<(double X, double Y, double Z)> Trail = new List<(double, double, double)>();
            public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz);
            public double PlanSpeed => Math.Sqrt(Vx * Vx + Vy * Vy);
        }

        public ParticleSystem(FieldData field = null) { if (field != null) Fields.Add(field); }

        /// <summary>Settings and forces the current run started with (a change restarts it).</summary>
        public string ForceKey = "";

        string Settings()
            => string.Join("|", Seed, Rate, Life, SpeedScale, Noise, Dt, MinConfidence, Response, Drag, Hold,
                           KeepOutside, Join, FieldKey, Fields.Count, Forces.Count, ForceKey);

        public void Reset()
        {
            _used = Settings();
            Time = 0; _nextId = 0; Alive.Clear();
            _rnd = new Random(Seed);
            int n = Fields.Count;
            _acc = new double[n];
            _cdf = new double[n][];
            _cells = new int[n][];
            for (int l = 0; l < n; l++)
            {
                // birth places: busier cells more (square root of density: quiet places still emit)
                var f = Fields[l];
                var cells = new List<int>();
                var w = new List<double>();
                double sum = 0;
                for (int k = 0; f != null && k < f.Count; k++)
                {
                    if (!f.Has[k] || f.Confidence[k] < MinConfidence) continue;
                    double wk = Math.Sqrt(Math.Max(f.Density[k], 0)) * Math.Max(f.Confidence[k], 1e-3);
                    if (wk <= 0) continue;
                    sum += wk; cells.Add(k); w.Add(sum);
                }
                _cells[l] = cells.ToArray();
                _cdf[l] = w.ToArray();
            }
        }

        /// <summary>Run to time t (seconds) with fixed steps; going back re-runs from 0.</summary>
        public void StepTo(double t)
        {
            if (_rnd == null || _used != Settings() || t < Time - 1e-9) Reset();
            while (Time + Dt <= t + 1e-9) Step();
        }

        public void Step()
        {
            if (_rnd == null) Reset();
            for (int l = 0; l < Fields.Count; l++)
            {
                _acc[l] += Rate * Dt;
                while (_acc[l] >= 1 && _cells[l].Length > 0)
                {
                    _acc[l] -= 1;
                    Spawn(l);
                }
            }
            foreach (var f in Forces) f.Prepare(this);
            int keep = (int)Math.Ceiling(TrailSeconds / Dt) + 1;
            for (int n = Alive.Count - 1; n >= 0; n--)
            {
                var p = Alive[n];
                bool ok = Move(p) && p.Age <= p.LifeSpan && p.Stalled <= 0.5;
                foreach (var f in Forces)
                    if (ok && !f.Constrain(this, p)) ok = false;
                if (!ok) { Alive.RemoveAt(n); continue; }
                p.Trail.Add((p.X, p.Y, p.Z));
                if (p.Trail.Count > keep) p.Trail.RemoveRange(0, p.Trail.Count - keep);
            }
            Time += Dt;
        }

        void Spawn(int layer)
        {
            var f = Fields[layer];
            var cdf = _cdf[layer];
            double r = _rnd.NextDouble() * cdf[cdf.Length - 1];
            int lo = 0, hi = cdf.Length - 1;
            while (lo < hi)
            {
                int m = (lo + hi) / 2;
                if (cdf[m] < r) lo = m + 1; else hi = m;
            }
            int k = _cells[layer][lo];
            int i = k % f.Nx, j = k / f.Nx;
            double lx = f.X0 + (i + _rnd.NextDouble() - 0.5) * f.Cell;
            double ly = f.Y0 + (j + _rnd.NextDouble() - 0.5) * f.Cell;
            var (x, y, z) = f.LocalToWorld(lx, ly, f.Z[k]);
            double life = Life * (0.7 + 0.6 * _rnd.NextDouble());
            if (!f.SampleWorld(x, y, z, out var s)) return;
            var p = new Particle
            {
                Id = _nextId++, Layer = layer, X = s.Gx, Y = s.Gy, Z = s.Z, LifeSpan = life, Smax = s.Smax,
                Vx = s.Vx * SpeedScale, Vy = s.Vy * SpeedScale, Vz = s.Vz * SpeedScale,
            };
            p.Trail.Add((p.X, p.Y, p.Z));
            Alive.Add(p);
        }

        /// <summary>One step of a particle; false when it left its field (and must not).</summary>
        bool Move(Particle p)
        {
            if (p.Bridge > 0) return Coast(p);
            var f = Fields[p.Layer];
            // the layer's frame here: along its ground (U, V) and across it (N; up for flat layers)
            bool inside = f.SampleWorld(p.X, p.Y, p.Z, out var a);
            if (!inside && !KeepOutside) return false;
            var (nx, ny, nz) = inside ? (a.Nx, a.Ny, a.Nz) : f.Place.Normal;
            var (ux, uy, uz) = f.Place.AxisU;
            var (wx, wy, wz) = f.Place.AxisV;
            if (f.Warped)
            {
                // along the bent ground: world X (or Y) laid onto it, and across
                double d = nx;
                (ux, uy, uz) = (1 - d * nx, -d * ny, -d * nz);
                double l = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                if (l < 0.1) { d = ny; (ux, uy, uz) = (-d * nx, 1 - d * ny, -d * nz); l = Math.Sqrt(ux * ux + uy * uy + uz * uz); }
                ux /= l; uy /= l; uz /= l;
                (wx, wy, wz) = (ny * uz - nz * uy, nz * ux - nx * uz, nx * uy - ny * ux);
            }
            double fx = inside ? a.Vx * SpeedScale : 0, fy = inside ? a.Vy * SpeedScale : 0, fz = inside ? a.Vz * SpeedScale : 0;

            double ax = 0, ay = 0, az = 0;
            foreach (var force in Forces) force.Apply(this, p, ref ax, ref ay, ref az);
            // forces across the ground act on the height (below), the rest along the ground
            double an = ax * nx + ay * ny + az * nz;
            ax -= an * nx; ay -= an * ny; az -= an * nz;
            // the particle's speed across the ground, and along it
            double vn = p.Vx * nx + p.Vy * ny + p.Vz * nz;
            double vx = p.Vx - vn * nx, vy = p.Vy - vn * ny, vz = p.Vz - vn * nz;

            // smooth wandering along the ground (Ornstein-Uhlenbeck, 1 s memory), scaled by the local speed
            if (Noise > 0)
            {
                double sigma = Noise * Math.Max(Math.Sqrt(fx * fx + fy * fy + fz * fz), 0.05);
                p.NoiseX += -p.NoiseX * Dt + sigma * Math.Sqrt(2 * Dt) * Gauss();
                p.NoiseY += -p.NoiseY * Dt + sigma * Math.Sqrt(2 * Dt) * Gauss();
            }
            double ox = p.NoiseX * ux + p.NoiseY * wx, oy = p.NoiseX * uy + p.NoiseY * wy, oz = p.NoiseX * uz + p.NoiseY * wz;

            if (Response <= 0)
            {
                // velocity mode: the field's velocity (midpoint) + what the forces pushed on top
                p.Ux += (ax - Drag * p.Ux) * Dt;
                p.Uy += (ay - Drag * p.Uy) * Dt;
                p.Uz += (az - Drag * p.Uz) * Dt;
                double ex = p.Ux + ox, ey = p.Uy + oy, ez = p.Uz + oz;
                double mx = p.X + (fx + ex) * Dt / 2, my = p.Y + (fy + ey) * Dt / 2, mz = p.Z + (fz + ez) * Dt / 2;
                if (f.SampleWorld(mx, my, mz, out var m)) { fx = m.Vx * SpeedScale; fy = m.Vy * SpeedScale; fz = m.Vz * SpeedScale; }
                else if (CanJoin) { }        // crossing the edge onto another layer: keep this velocity
                else if (!KeepOutside) return false;
                else { fx = 0; fy = 0; fz = 0; }
                vx = fx + ex;
                vy = fy + ey;
                vz = fz + ez;
            }
            else
            {
                // force mode: inertia, reaching the field's velocity within ~Response seconds
                double tx = inside ? fx + ox : vx;        // outside: keep going (only forces act)
                double ty = inside ? fy + oy : vy;
                double tz = inside ? fz + oz : vz;
                vx += ((tx - vx) / Response + ax) * Dt;
                vy += ((ty - vy) / Response + ay) * Dt;
                vz += ((tz - vz) / Response + az) * Dt;
            }
            p.X += vx * Dt;
            p.Y += vy * Dt;
            p.Z += vz * Dt;

            bool now = f.SampleWorld(p.X, p.Y, p.Z, out var e);
            if (!now && CanJoin)
            {
                // left its field: carry on onto another layer whose ground is right here, or across
                // a short gap to one ahead (straight on)
                double tx = vx + vn * nx, ty = vy + vn * ny, tz = vz + vn * nz;
                int to = JoinTarget(p.Layer, p.X, p.Y, p.Z, tx, ty, tz, out e);
                if (to >= 0 || Ahead(p.Layer, p.X, p.Y, p.Z, tx, ty, tz))
                {
                    p.Vx = tx; p.Vy = ty; p.Vz = tz;
                    p.Age += Dt;
                    if (to >= 0) HandOver(p, to, e);
                    else p.Bridge = 1e-9;
                    return true;
                }
            }
            if (!now && !KeepOutside) return false;

            // height above the layer's ground: Hold 1 = on it, below 1 = a spring, 0 = free
            if (now && f.Warped) (nx, ny, nz) = (e.Nx, e.Ny, e.Nz);
            double h = now ? (p.X - e.Gx) * nx + (p.Y - e.Gy) * ny + (p.Z - e.Z) * nz : 0;
            if (Hold >= 0.999 && an == 0)
            {
                vn = 0;
                p.X -= h * nx; p.Y -= h * ny; p.Z -= h * nz;
            }
            else
            {
                double k = 60 * Hold * Hold, c = 2 * Math.Sqrt(k) + 0.2 * Drag;
                vn += (an - k * h - c * vn) * Dt;
                p.X += vn * Dt * nx; p.Y += vn * Dt * ny; p.Z += vn * Dt * nz;
            }
            p.Vx = vx + vn * nx; p.Vy = vy + vn * ny; p.Vz = vz + vn * nz;
            if (now) p.Smax = e.Smax;
            p.Age += Dt;
            double along = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            p.Stalled = Forces.Count == 0 && along < 0.02 * Math.Max(p.Smax * f.Scale, 0.5) ? p.Stalled + Dt : 0;
            return true;
        }

        bool CanJoin => Join > 0 && Fields.Count > 1;

        /// <summary>Crossing a gap between layers: straight on until another layer's ground is
        /// reached (it joins that layer) or Join metres are used up.</summary>
        bool Coast(Particle p)
        {
            p.X += p.Vx * Dt; p.Y += p.Vy * Dt; p.Z += p.Vz * Dt;
            p.Bridge += p.Speed * Dt;
            p.Age += Dt;
            int to = JoinTarget(p.Layer, p.X, p.Y, p.Z, p.Vx, p.Vy, p.Vz, out var e);
            if (to >= 0) { HandOver(p, to, e); return true; }
            if (p.Bridge <= Join) return true;
            p.Bridge = 0;
            return KeepOutside;
        }

        /// <summary>Particle p joins layer `to` (e: the sample there): it takes that field's flow
        /// (or keeps its speed along the new ground, with inertia) and sits on its ground.</summary>
        void HandOver(Particle p, int to, Sample e)
        {
            p.Layer = to; p.Joined++; p.Bridge = 0;
            double nx = e.Nx, ny = e.Ny, nz = e.Nz;
            double un = p.Ux * nx + p.Uy * ny + p.Uz * nz;
            p.Ux -= un * nx; p.Uy -= un * ny; p.Uz -= un * nz;
            if (Response <= 0)
            {
                p.Vx = e.Vx * SpeedScale + p.Ux; p.Vy = e.Vy * SpeedScale + p.Uy; p.Vz = e.Vz * SpeedScale + p.Uz;
            }
            else
            {
                double vn = p.Vx * nx + p.Vy * ny + p.Vz * nz;
                p.Vx -= vn * nx; p.Vy -= vn * ny; p.Vz -= vn * nz;
            }
            if (Hold >= 0.999)
            {
                double h = (p.X - e.Gx) * nx + (p.Y - e.Gy) * ny + (p.Z - e.Z) * nz;
                p.X -= h * nx; p.Y -= h * ny; p.Z -= h * nz;
            }
            p.Smax = e.Smax;
        }

        /// <summary>Another layer's ground within Join straight ahead along (vx, vy, vz)?</summary>
        bool Ahead(int layer, double x, double y, double z, double vx, double vy, double vz)
        {
            double sp = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (sp < 1e-9) return false;
            double step = double.MaxValue;
            foreach (var g in Fields) step = Math.Min(step, g.WorldCell / 2);
            step = Math.Max(step, 0.1);
            for (double d = step; d <= Join + 1e-9; d += step)
                if (JoinTarget(layer, x + vx / sp * d, y + vy / sp * d, z + vz / sp * d, vx, vy, vz, out _) >= 0) return true;
            return false;
        }

        /// <summary>The layer (other than `layer`) a particle at (x, y, z) moving along (vx, vy,
        /// vz) can join: one whose ground is within Join and whose flow carries on its way (not
        /// against it) and on into that field; the closest. -1: none.</summary>
        int JoinTarget(int layer, double x, double y, double z, double vx, double vy, double vz, out Sample at)
        {
            at = default;
            int best = -1;
            double bestH = Join;
            for (int l = 0; l < Fields.Count; l++)
            {
                if (l == layer) continue;
                var g = Fields[l];
                if (!g.SampleWorld(x, y, z, out var s) || s.Speed < 1e-9) continue;
                double h = Math.Abs((x - s.Gx) * s.Nx + (y - s.Gy) * s.Ny + (z - s.Z) * s.Nz);
                if (h >= bestH || s.Vx * vx + s.Vy * vy + s.Vz * vz <= 0) continue;
                // a cell along its flow is still in it
                double step = Math.Max(g.WorldCell, s.Speed * SpeedScale * Dt) / s.Speed;
                if (!g.SampleWorld(s.Gx + s.Vx * step, s.Gy + s.Vy * step, s.Z + s.Vz * step, out _)) continue;
                best = l; bestH = h; at = s;
            }
            return best;
        }

        double Gauss()
        {
            double u1 = 1.0 - _rnd.NextDouble(), u2 = _rnd.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
