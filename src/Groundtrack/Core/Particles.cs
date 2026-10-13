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
    /// tilted ones). Deterministic for a seed: the state at time t is the same however you get
    /// there, so a timeline can scrub.</summary>
    public sealed class ParticleSystem
    {
        public List<FieldData> Fields = new List<FieldData>();
        public double Rate = 200, Life = 20, SpeedScale = 1, Noise = 0, TrailSeconds = 2, Dt = 1 / 30.0;
        public double MinConfidence = 0.0, Response = 0, Drag = 1.0, Hold = 1.0;
        public bool KeepOutside;
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
            public int Id, Layer;
            public double X, Y, Z, Vx, Vy, Vz, Ux, Uy, Uz, Age, LifeSpan, Smax, NoiseX, NoiseY, Stalled;
            public readonly List<(double X, double Y, double Z)> Trail = new List<(double, double, double)>();
            public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz);
            public double PlanSpeed => Math.Sqrt(Vx * Vx + Vy * Vy);
        }

        public ParticleSystem(FieldData field = null) { if (field != null) Fields.Add(field); }

        /// <summary>Settings and forces the current run started with (a change restarts it).</summary>
        public string ForceKey = "";

        string Settings()
            => string.Join("|", Seed, Rate, Life, SpeedScale, Noise, Dt, MinConfidence, Response, Drag, Hold,
                           KeepOutside, FieldKey, Fields.Count, Forces.Count, ForceKey);

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
            var (x, y, z) = f.Place.Apply(lx, ly, f.Z[k]);
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
            var f = Fields[p.Layer];
            // the layer's frame: along its ground (U, V) and across it (N; up for flat layers)
            var (nx, ny, nz) = f.Place.Normal;
            var (ux, uy, uz) = f.Place.AxisU;
            var (wx, wy, wz) = f.Place.AxisV;
            bool inside = f.SampleWorld(p.X, p.Y, p.Z, out var a);
            if (!inside && !KeepOutside) return false;
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

            // height above the layer's ground: Hold 1 = on it, below 1 = a spring, 0 = free
            bool now = f.SampleWorld(p.X, p.Y, p.Z, out var e);
            if (!now && !KeepOutside) return false;
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

        double Gauss()
        {
            double u1 = 1.0 - _rnd.NextDouble(), u2 = _rnd.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
