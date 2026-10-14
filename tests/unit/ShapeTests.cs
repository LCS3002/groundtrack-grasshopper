using System;
using System.Linq;
using Groundtrack.Core;
using Xunit;

namespace Groundtrack.Tests
{
    /// <summary>Fields bent into 3D: morphs, lofts between layers, warps, landscape, forces, ribbons.</summary>
    public class ShapeTests
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

        /// <summary>A strip rolled up into part of a cylinder of radius R about the line y = 0,
        /// z = R (x along the axis).</summary>
        static (double, double, double) Roll(double x, double y, double z, double R)
        {
            double th = y / R;
            return (x, (R - z) * Math.Sin(th), R - (R - z) * Math.Cos(th));
        }

        [Fact]
        public void A_bent_field_carries_particles_and_streamlines_along_its_curved_sheet()
        {
            const double R = 10;
            var f = Uniform(30, 40, 0.5, 0.3, 1.0);        // 15 x 20 m, flowing mostly across the roll
            var g = f.Morphed((x, y, z) => Roll(x, y, z, R));
            Assert.True(g.Warped);
            double OffCylinder(double x, double y, double z) => Math.Abs(Math.Sqrt(y * y + (z - R) * (z - R)) - R);
            // cells sit on the cylinder, velocities along it, same speed
            for (int k = 0; k < g.Count; k += 7)
            {
                var (x, y, z) = g.CellWorld(k);
                Assert.True(OffCylinder(x, y, z) < 1e-4);
                var (vx, vy, vz) = g.CellVelocity(k);
                Assert.Equal(Math.Sqrt(0.3 * 0.3 + 1), Math.Sqrt(vx * vx + vy * vy + vz * vz), 2);
            }
            var ps = new ParticleSystem(g) { Rate = 100, Life = 30, Seed = 3 };
            ps.StepTo(8);
            Assert.True(ps.Alive.Count > 100);
            foreach (var p in ps.Alive)
            {
                Assert.True(OffCylinder(p.X, p.Y, p.Z) < 0.02, $"particle {OffCylinder(p.X, p.Y, p.Z):0.000} m off the sheet");
                Assert.True(g.SampleWorld(p.X, p.Y, p.Z, out _));
                Assert.Equal(Math.Sqrt(0.3 * 0.3 + 1), p.Speed, 1);
            }
            Assert.Contains(ps.Alive, p => p.Z > 3);         // they climbed the roll
            var lines = Streamlines.Trace(g, 1.5);
            Assert.True(lines.Count > 3);
            foreach (var l in lines)
                for (int k = 0; k < l.Count; k++)
                    Assert.True(OffCylinder(l.X[k], l.Y[k], l.Z[k]) < 0.02);
        }

        [Fact]
        public void Outlines_go_round_the_data_counter_clockwise()
        {
            var f = Uniform(20, 10, 1.0, 1, 0);
            var loops = Shape.Outlines(f);
            Assert.Single(loops);
            var l = loops[0];
            double area = 0, per = 0;
            for (int k = 0; k < l.Count; k++)
            {
                var a = l[k];
                var b = l[(k + 1) % l.Count];
                area += a.X * b.Y - b.X * a.Y;
                per += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            }
            Assert.True(area > 0);                                   // counter-clockwise
            Assert.InRange(area / 2, 180, 200);                      // 20 x 10 m, corners rounded
            Assert.InRange(per, 55, 60);
        }

        [Fact]
        public void A_loft_folds_from_one_layer_onto_the_next_and_particles_follow_it()
        {
            var top = Uniform(20, 10, 1.0, 1.2, 0);                 // flows east
            var bottom = Uniform(20, 10, 1.0, -1.2, 0);             // 6 m below, flows back west
            bottom.Place = new Placement(1, 0, 0, 1, 0, 0, 1, -6);
            var fold = Shape.Loft(top, bottom);
            Assert.NotNull(fold);
            Assert.True(fold.Warped);
            // the fold starts on the top layer's height and ends on the bottom's
            var (_, _, z0) = fold.CellWorld(0);
            var (_, _, z1) = fold.CellWorld((fold.Ny - 1) * fold.Nx);
            Assert.Equal(0, z0, 3);
            Assert.Equal(-6, z1, 3);
            // particles run east on top, round the fold and back west below
            var ps = new ParticleSystem { Rate = 100, Life = 80, Seed = 1, Join = 2 };
            ps.Fields.Add(top); ps.Fields.Add(fold); ps.Fields.Add(bottom);
            int below = 0;
            var born = new System.Collections.Generic.Dictionary<int, int>();
            for (int t = 1; t <= 40; t++)
            {
                ps.StepTo(t);
                foreach (var p in ps.Alive)
                {
                    if (!born.ContainsKey(p.Id)) born[p.Id] = p.Layer;
                    if (born[p.Id] == 0 && p.Layer == 2) below++;
                }
            }
            Assert.True(below > 0, "no particle came round the fold onto the lower layer");
        }

        [Fact]
        public void Warps_twist_wave_and_bulge_as_asked()
        {
            var f = Uniform(20, 10, 1.0, 1, 0);
            double[] o = { 0, 0, 0 }, x = { 1, 0, 0 }, up = { 0, 0, 1 };
            var twist = Shape.Warp(Shape.WarpMode.Twist, o, x, 19, up, 90, 0);
            var (_, ty, tz) = twist(19, 5, 0);                        // the far end turned 90 deg
            Assert.Equal(0, ty, 6); Assert.Equal(5, tz, 6);
            var wave = Shape.Warp(Shape.WarpMode.Wave, o, x, 19, up, 2, 8);
            Assert.Equal(2, wave(2, 3, 0).Z, 6);                     // a quarter wavelength in: the crest
            var bulge = Shape.Warp(Shape.WarpMode.Bulge, new[] { 10.0, 5, 0 }, x, 19, up, 4, 6);
            Assert.Equal(4, bulge(10, 5, 0).Z, 6);
            Assert.Equal(0, bulge(17, 5, 0).Z, 6);
            var bend = Shape.Warp(Shape.WarpMode.Bend, o, x, 20, up, 180, 0);
            var (bx, _, bz) = bend(20, 0, 0);                        // bent half round: back over the start
            Assert.Equal(0, bx, 6); Assert.Equal(2 * 20 / Math.PI, bz, 6);
            var g = f.Morphed(wave);
            var ps = new ParticleSystem(g) { Rate = 50, Life = 30, Seed = 2 };
            ps.StepTo(6);
            Assert.Contains(ps.Alive, p => p.Z > 1);                 // riding the waves
        }

        [Fact]
        public void Landscape_softens_steps_and_raises_busy_ground()
        {
            var f = Uniform(40, 10, 1.0, 1, 0);
            for (int k = 0; k < f.Count; k++) if (k % f.Nx >= 20) f.Z[k] = 6;    // a 6 m step
            var g = Shape.Lift(f, Shape.Landscape(f, 8, "none", 0));
            double Step(FieldData h)
            {
                double m = 0;
                for (int i = 1; i < h.Nx; i++) m = Math.Max(m, Math.Abs(h.Z[5 * h.Nx + i] - h.Z[5 * h.Nx + i - 1]));
                return m;
            }
            Assert.Equal(6, Step(f), 6);
            Assert.True(Step(g) < 2, $"softened step {Step(g):0.00} m");
            var busy = Uniform(20, 20, 1.0, 1, 0);
            busy.Density[10 * 20 + 10] = 100;
            var h2 = Shape.Lift(busy, Shape.Landscape(busy, 0, "density", 5));
            Assert.Equal(5, h2.Z[10 * 20 + 10], 3);                  // the busiest point rises the most
            Assert.True(h2.Z[0] < 1);
        }

        [Fact]
        public void Forces_bend_the_flow_and_keep_its_speed()
        {
            var f = Uniform(20, 20, 1.0, 1, 0);
            var axis = new AxisForce { Ox = 10, Oy = 10, Oz = 0, Dx = 0, Dy = 0, Dz = 1, Radius = 30, Orbit = 3, Suction = 0, Lift = 0 };
            var g = Shape.ApplyForces(f, new IForce[] { axis }, 1.0, true);
            int k = 10 * 20 + 15;                                    // east of the axis: the orbit pushes north
            Assert.True(g.Vy[k] > 0.3);
            Assert.Equal(1, Math.Sqrt(g.Vx[k] * g.Vx[k] + g.Vy[k] * g.Vy[k]), 4);
            int w = 10 * 20 + 5;                                     // west of it: south
            Assert.True(g.Vy[w] < -0.3);
        }

        [Fact]
        public void Fields_fuse_only_where_they_come_close()
        {
            var a = Uniform(20, 20, 1.0, 1.2, 0);                  // x 0..19, flowing east
            var b = Uniform(20, 20, 1.0, 1.2, 0, 26, 0);           // x 26..45: a 6 m gap
            b.Place = new Placement(1, 0, 0, 1, 0, 0, 1, 3);       // and 3 m up
            Assert.Empty(Shape.Fuse(a, b, 4, 0.6));                // out of reach
            var fu = Shape.Fuse(a, b, 9, 0.6);
            Assert.Single(fu);
            var (x0, _, _, x1, _, _) = fu[0].WorldBox();
            Assert.True(x0 > 15 && x1 < 30, $"the fusion spans x {x0:0.0}..{x1:0.0}: only the facing sides");
            var ps = new ParticleSystem { Rate = 100, Life = 80, Seed = 1, Join = 2 };
            ps.Fields.Add(a); ps.Fields.Add(fu[0]); ps.Fields.Add(b);
            var born = new System.Collections.Generic.Dictionary<int, int>();
            int across = 0;
            for (int t = 1; t <= 40; t++)
            {
                ps.StepTo(t);
                foreach (var p in ps.Alive)
                {
                    if (!born.ContainsKey(p.Id)) born[p.Id] = p.Layer;
                    if (born[p.Id] == 0 && p.Layer == 2) across++;
                }
            }
            Assert.True(across > 0, "no particle crossed the fusion");
            // only where asked: a mask that excludes everything
            Assert.Empty(Shape.Fuse(a, b, 9, 0.6, q => false));
        }

        [Fact]
        public void Sculpt_presses_a_hollow_and_turns_the_flow_into_it()
        {
            var f = Uniform(40, 40, 1.0, 1, 0);
            var w = new double[f.Count];
            for (int k = 0; k < f.Count; k++)
            {
                double dx = k % 40 - 20, dy = k / 40 - 20, r = Math.Sqrt(dx * dx + dy * dy);
                w[k] = r < 10 ? 1 - r / 10 : 0;
            }
            var g = Shape.Sculpt(f, w, -4, 1);
            Assert.Equal(-4, g.Z[20 * 40 + 20], 4);                // the hollow's bottom
            Assert.Equal(0, g.Z[0], 6);                            // untouched far away
            int k2 = 20 * 40 + 14;                                 // west of the centre: flows east, into it
            int k3 = 26 * 40 + 20;                                 // north of it: turned south, down the slope
            Assert.True(g.Vy[k3] < -0.3, $"vy {g.Vy[k3]:0.00}");
            Assert.True(g.Vx[k2] > 0.9);
            Assert.Equal(1, Math.Sqrt(g.Vx[k3] * g.Vx[k3] + g.Vy[k3] * g.Vy[k3]), 3);
        }

        [Fact]
        public void Saturation_makes_gradients_punchier_and_1_leaves_them()
        {
            var g = Gradient.Preset("groundtrack");
            Assert.Same(g, g.Adjusted(1, 1));
            double Sat((byte R, byte G, byte B) c) { double mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B)); return mx > 0 ? (mx - mn) / mx : 0; }
            var h = g.Adjusted(1.6, 1);
            for (double t = 0; t <= 1; t += 0.1)
                Assert.True(Sat(h.At(t)) >= Sat(g.At(t)) - 1e-9);
            Assert.True(Sat(h.At(0.5)) > Sat(g.At(0.5)) + 0.1);
            Assert.Equal("vivid", Gradient.Preset("vivid").Name);
        }

        [Fact]
        public void A_ribbon_runs_along_its_curve()
        {
            var curve = Enumerable.Range(0, 41).Select(i => (X: i * 0.5, Y: 0.0, Z: Math.Sin(i * 0.5 / 20 * Math.PI) * 5)).ToList();
            var r = Shape.Ribbon(curve, 4, 1.5, t => new[] { 0.0, 0, 1 });
            Assert.True(r.Warped);
            var ps = new ParticleSystem(r) { Rate = 50, Life = 30, Seed = 1 };
            ps.StepTo(5);
            Assert.True(ps.Alive.Count > 20);
            foreach (var p in ps.Alive)
            {
                Assert.Equal(1.5, p.Speed, 1);
                Assert.True(Math.Abs(p.Y) <= 2.01);                   // within the ribbon's width
            }
            Assert.Contains(ps.Alive, p => p.Z > 3);                 // over the hump
        }
    }
}
