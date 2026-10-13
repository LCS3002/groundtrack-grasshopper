using System;
using System.Collections.Generic;
using System.Linq;
using Groundtrack.Core;
using Xunit;

namespace Groundtrack.Tests
{
    public class ForceTests
    {
        static FieldData Uniform(int nx, int ny, double cell, double vx, double vy, double x0 = 0, double y0 = 0)
        {
            var f = FieldData.Empty(nx, ny, cell, x0, y0);
            for (int k = 0; k < f.Count; k++)
            {
                f.Has[k] = true; f.Vx[k] = (float)vx; f.Vy[k] = (float)vy;
                f.Speed[k] = (float)Math.Sqrt(vx * vx + vy * vy); f.Density[k] = 1;
                f.Confidence[k] = 1; f.Smax[k] = 2.5f;
            }
            return f;
        }

        [Fact]
        public void Axis_force_swirls_particles_around_the_axis()
        {
            var f = Uniform(80, 80, 0.5, 0, 0, -20, -20);      // still field, centred on 0
            var s = new ParticleSystem(f) { Rate = 200, Life = 30, Seed = 2 };
            s.Forces.Add(new AxisForce { Radius = 15, Orbit = 2 });
            s.StepTo(3);
            var near = s.Alive.Where(p => Math.Sqrt(p.X * p.X + p.Y * p.Y) < 12).ToList();
            Assert.True(near.Count > 50);
            int ccw = near.Count(p => p.X * p.Vy - p.Y * p.Vx > 0);   // counter-clockwise about +Z
            Assert.True(ccw > 0.9 * near.Count, $"{ccw} of {near.Count} turn the right way");
        }

        [Fact]
        public void Attract_pulls_in_and_negative_strength_pushes_out()
        {
            var f = Uniform(80, 80, 0.5, 0, 0, -20, -20);
            double Mean(double strength)
            {
                var s = new ParticleSystem(f) { Rate = 200, Life = 30, Seed = 4 };
                s.Forces.Add(new PointForce { Points = { (0, 0, 0) }, Strength = strength, Radius = 30 });
                s.StepTo(4);
                return s.Alive.Average(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
            }
            double none = Mean(0), pulled = Mean(2), pushed = Mean(-2);
            Assert.True(pulled < none - 1, $"attract {pulled:0.0} vs {none:0.0}");
            Assert.True(pushed > none + 1, $"repel {pushed:0.0} vs {none:0.0}");
        }

        [Fact]
        public void Response_zero_follows_the_field_and_large_response_lags()
        {
            var f = Uniform(200, 20, 0.5, 1.5, 0);
            var follow = new ParticleSystem(f) { Rate = 50, Life = 30, Seed = 1, Response = 0 };
            follow.StepTo(3);
            Assert.All(follow.Alive, p => Assert.Equal(1.5, p.PlanSpeed, 6));
            var slow = new ParticleSystem(f) { Rate = 50, Life = 30, Seed = 1, Response = 2 };
            slow.StepTo(0.5);
            var young = slow.Alive.Where(p => p.Age < 0.3).ToList();
            Assert.NotEmpty(young);
            Assert.All(young, p => Assert.True(Math.Abs(p.PlanSpeed - 1.5) < 0.3));  // born at the field speed
            var quick = new ParticleSystem(f) { Rate = 50, Life = 30, Seed = 1, Response = 0.1 };
            quick.Forces.Add(new UniformForce { Fy = 3 });
            slow.Forces.Add(new UniformForce { Fy = 3 });
            quick.StepTo(4); slow.StepTo(4);
            // a side force bends inert particles more (equilibrium side speed = force * response)
            Assert.True(slow.Alive.Average(p => p.Vy) > 3 * quick.Alive.Average(p => p.Vy));
        }

        [Fact]
        public void Interacting_layers_leave_their_planes_and_intertwine()
        {
            var a = Uniform(80, 30, 0.5, 1.0, 0.2);
            var b = Uniform(80, 30, 0.5, 1.0, -0.2);
            b.Place = new Placement(1, 0, 0, 1, 0, 0, 1, 4);   // 4 m above
            ParticleSystem last = null;
            string Hist() => string.Join(" ", Enumerable.Range(-2, 9).Select(b => $"{b}:{last.Alive.Count(p => p.Z >= b && p.Z < b + 1)}"));
            double Between(bool interact)
            {
                var s = new ParticleSystem { Rate = 150, Life = 30, Seed = 5, Hold = 0.15 };
                s.Fields.Add(a); s.Fields.Add(b);
                if (interact) s.Forces.Add(new InteractForce { Between = InteractForce.Who.Others, Radius = 6, Cohesion = 3, Separation = 0.2 });
                s.StepTo(6);
                last = s;
                // off their own layer's plane (0 m or 4 m) by more than half a metre
                return s.Alive.Count(p => Math.Abs(p.Z - (p.Layer == 0 ? 0 : 4)) > 0.5) / (double)s.Alive.Count;
            }
            Assert.Equal(0, Between(false), 6);
            double mixed = Between(true);
            Assert.True(mixed > 0.3, $"{mixed:P0} off their layer; z: {Hist()}");
        }

        [Fact]
        public void Obstacles_are_never_entered_and_the_flow_goes_round()
        {
            var f = Uniform(120, 40, 0.5, 1.5, 0);
            var box = new FootprintObstacle(new List<(double, double)> { (25, 5), (35, 5), (35, 15), (25, 15) });
            var s = new ParticleSystem(f) { Rate = 300, Life = 40, Seed = 7 };
            s.Forces.Add(new ObstacleForce { Obstacles = { box }, Margin = 1.5 });
            for (int n = 0; n < 300; n++)
            {
                s.Step();
                Assert.DoesNotContain(s.Alive, p => box.SignedDistance(p.X, p.Y, p.Z, out _, out _, out _) < 0);
            }
            Assert.Contains(s.Alive, p => p.X > 36 && p.Y > 6 && p.Y < 14);        // got behind it
        }

        [Fact]
        public void Field_from_curves_flows_along_them()
        {
            var line = new List<IList<(double, double, double)>> { new List<(double, double, double)> { (0, 0, 2), (30, 0, 2) } };
            var f = CurveField.Make(line, 1.3, 3);
            Assert.True(f.Sample(15, 1, out var s));
            Assert.Equal(1.3, s.Vx, 6); Assert.Equal(0, s.Vy, 6); Assert.Equal(2, s.Z, 6);
            Assert.False(f.Sample(15, 3.5, out _));
            var lines = Streamlines.Trace(f, 1.0);
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.True(Math.Abs(l.Y[l.Count - 1] - l.Y[0]) < 1e-6));
        }

        [Fact]
        public void Field_force_turns_particles_towards_another_flow()
        {
            var east = Uniform(80, 80, 0.5, 1.5, 0);
            var north = Uniform(80, 80, 0.5, 0, 1.5);
            var s = new ParticleSystem(east) { Rate = 100, Life = 30, Seed = 3, Response = 0.5 };
            s.Forces.Add(new FieldForce { Field = north, Strength = 3 });
            s.StepTo(4);
            Assert.True(s.Alive.Average(p => p.Vy) > 0.5);
        }

        [Fact]
        public void Force_lifts_free_particles_and_noise_is_repeatable()
        {
            var f = Uniform(60, 20, 0.5, 1.0, 0);
            var s = new ParticleSystem(f) { Rate = 80, Life = 30, Seed = 9, Hold = 0.2 };
            s.Forces.Add(new UniformForce { Fz = 1, Noise = 0.5 });
            s.StepTo(4);
            Assert.True(s.Alive.Average(p => p.Z) > 0.05);
            var at = s.Alive.Select(p => (p.Id, Math.Round(p.X, 9), Math.Round(p.Z, 9))).ToList();
            s.StepTo(1); s.StepTo(4);
            Assert.Equal(at, s.Alive.Select(p => (p.Id, Math.Round(p.X, 9), Math.Round(p.Z, 9))).ToList());
        }
    }
}
