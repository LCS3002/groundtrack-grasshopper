using System;
using System.Collections.Generic;

namespace Groundtrack.Core
{
    /// <summary>Particles following a field, like a Houdini POP network: born where people or
    /// vehicles were (busier cells more), moved along the field (midpoint steps), with optional
    /// smooth wandering (noise), killed when they leave the field, stall or reach their life.
    /// Deterministic for a seed: the state at time t is the same however you get there, so a
    /// timeline can scrub back and forth.</summary>
    public sealed class ParticleSystem
    {
        public FieldData Field;
        public double Rate = 200, Life = 20, SpeedScale = 1, Noise = 0, TrailSeconds = 2, Dt = 1 / 30.0;
        public double MinConfidence = 0.0;
        public int Seed = 1;

        public double Time { get; private set; }
        public readonly List<Particle> Alive = new List<Particle>();
        Random _rnd;
        double _acc;
        int _nextId;
        double[] _cdf;
        int[] _cells;

        public sealed class Particle
        {
            public int Id;
            public double X, Y, Z, Vx, Vy, Age, LifeSpan, Smax, NoiseX, NoiseY, Stalled;
            public readonly List<(double X, double Y, double Z)> Trail = new List<(double, double, double)>();
            public int TrailStart;   // index of the oldest trail point still kept
            public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy);
        }

        public ParticleSystem(FieldData field) { Field = field; }

        (int, double, double, double, double, double, double, string) _used;

        /// <summary>Which field this is (a new time slice of the same field keeps the particles).</summary>
        public string FieldKey = "";

        /// <summary>The settings the current run was started with.</summary>
        (int, double, double, double, double, double, double, string) Settings
            => (Seed, Rate, Life, SpeedScale, Noise, Dt, MinConfidence, FieldKey);

        public void Reset()
        {
            _used = Settings;
            Time = 0; _acc = 0; _nextId = 0; Alive.Clear();
            _rnd = new Random(Seed);
            // birth places: busier cells more (square root of density, so quiet places still emit)
            var cells = new List<int>();
            var w = new List<double>();
            double sum = 0;
            if (Field != null)
                for (int k = 0; k < Field.Count; k++)
                {
                    if (!Field.Has[k] || Field.Confidence[k] < MinConfidence) continue;
                    double wk = Math.Sqrt(Math.Max(Field.Density[k], 0)) * Math.Max(Field.Confidence[k], 1e-3);
                    if (wk <= 0) continue;
                    sum += wk; cells.Add(k); w.Add(sum);
                }
            _cells = cells.ToArray();
            _cdf = w.ToArray();
        }

        /// <summary>Run to time t (seconds) with fixed steps; going back re-runs from 0.</summary>
        public void StepTo(double t)
        {
            if (_rnd == null || !_used.Equals(Settings) || t < Time - 1e-9) Reset();
            while (Time + Dt <= t + 1e-9) Step();
        }

        public void Step()
        {
            // births
            _acc += Rate * Dt;
            while (_acc >= 1 && _cells.Length > 0)
            {
                _acc -= 1;
                Spawn();
            }
            double trailKeep = TrailSeconds;
            for (int n = Alive.Count - 1; n >= 0; n--)
            {
                var p = Alive[n];
                if (!Move(p) || p.Age > p.LifeSpan || p.Stalled > 0.5)
                {
                    Alive.RemoveAt(n);
                    continue;
                }
                p.Trail.Add((p.X, p.Y, p.Z));
                int keep = (int)Math.Ceiling(trailKeep / Dt) + 1;
                if (p.Trail.Count > keep) p.Trail.RemoveRange(0, p.Trail.Count - keep);
            }
            Time += Dt;
        }

        void Spawn()
        {
            double r = _rnd.NextDouble() * _cdf[_cdf.Length - 1];
            int lo = 0, hi = _cdf.Length - 1;
            while (lo < hi)
            {
                int m = (lo + hi) / 2;
                if (_cdf[m] < r) lo = m + 1; else hi = m;
            }
            int k = _cells[lo];
            int i = k % Field.Nx, j = k / Field.Nx;
            double lx = Field.X0 + (i + _rnd.NextDouble() - 0.5) * Field.Cell;
            double ly = Field.Y0 + (j + _rnd.NextDouble() - 0.5) * Field.Cell;
            var (x, y, z) = Field.Place.Apply(lx, ly, Field.Z[k]);
            double life = Life * (0.7 + 0.6 * _rnd.NextDouble());
            var p = new Particle { Id = _nextId++, X = x, Y = y, Z = z, LifeSpan = life, Smax = Field.Smax[k] };
            if (!Field.Sample(x, y, out var s)) return;
            p.Vx = s.Vx * SpeedScale; p.Vy = s.Vy * SpeedScale; p.Z = s.Z; p.Smax = s.Smax;
            p.Trail.Add((p.X, p.Y, p.Z));
            Alive.Add(p);
        }

        /// <summary>One midpoint step; false when the particle left the field.</summary>
        bool Move(Particle p)
        {
            if (!Field.Sample(p.X, p.Y, out var a)) return false;
            double ax = a.Vx * SpeedScale, ay = a.Vy * SpeedScale;
            if (Noise > 0)
            {
                // smooth wandering (Ornstein-Uhlenbeck, 1 s memory), scaled by the local speed
                double tau = 1.0, sigma = Noise * Math.Max(a.Speed * SpeedScale, 0.05);
                p.NoiseX += -p.NoiseX / tau * Dt + sigma * Math.Sqrt(2 * Dt / tau) * Gauss();
                p.NoiseY += -p.NoiseY / tau * Dt + sigma * Math.Sqrt(2 * Dt / tau) * Gauss();
            }
            double mx = p.X + (ax + p.NoiseX) * Dt / 2, my = p.Y + (ay + p.NoiseY) * Dt / 2;
            if (!Field.Sample(mx, my, out var m)) return false;
            p.Vx = m.Vx * SpeedScale + p.NoiseX;
            p.Vy = m.Vy * SpeedScale + p.NoiseY;
            p.X += p.Vx * Dt;
            p.Y += p.Vy * Dt;
            if (!Field.Sample(p.X, p.Y, out var e)) return false;
            p.Z = e.Z;
            p.Smax = e.Smax;
            p.Age += Dt;
            p.Stalled = p.Speed < 0.02 * Math.Max(e.Smax, 0.5) ? p.Stalled + Dt : 0;
            return true;
        }

        double Gauss()
        {
            double u1 = 1.0 - _rnd.NextDouble(), u2 = _rnd.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
