using System;
using System.Linq;
using Groundtrack.Core;
using Xunit;

namespace Groundtrack.Tests
{
    /// <summary>Fields rotated about any axis: they tilt / stand up, their flow with them.</summary>
    public class TiltTests
    {
        static FieldData Uniform(int nx, int ny, double cell, double vx, double vy, float z = 0)
        {
            var f = FieldData.Empty(nx, ny, cell, 0, 0);
            for (int k = 0; k < f.Count; k++)
            {
                f.Has[k] = true; f.Vx[k] = (float)vx; f.Vy[k] = (float)vy; f.Z[k] = z;
                f.Speed[k] = (float)Math.Sqrt(vx * vx + vy * vy); f.Density[k] = 1;
                f.Confidence[k] = 1; f.Smax[k] = 2.5f;
            }
            return f;
        }

        /// <summary>Rotation about the X axis through (0, cy, cz), as a 3x4 matrix.</summary>
        static double[,] RotX(double deg, double cy = 0, double cz = 0)
        {
            double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return new[,] { { 1, 0, 0, 0 }, { 0, c, -s, cy - (c * cy - s * cz) }, { 0, s, c, cz - (s * cy + c * cz) } };
        }

        static (double, double, double) Mul(double[,] m, double x, double y, double z)
            => (m[0, 0] * x + m[0, 1] * y + m[0, 2] * z + m[0, 3], m[1, 0] * x + m[1, 1] * y + m[1, 2] * z + m[1, 3],
                m[2, 0] * x + m[2, 1] * y + m[2, 2] * z + m[2, 3]);

        [Fact]
        public void Rotating_about_X_tilts_the_field_without_squashing_it()
        {
            var r = RotX(30);
            var p = Placement.Identity.Then(r);
            Assert.True(p.Tilted);
            Assert.Equal(1, p.Det, 9);                                  // same size in its plane
            foreach (var (x, y, z) in new[] { (3.0, 4.0, 0.0), (-2.0, 7.0, 1.5), (10.0, 0.0, -1.0) })
            {
                var (ax, ay, az) = p.Apply(x, y, z);
                var (ex, ey, ez) = Mul(r, x, y, z);
                Assert.Equal(ex, ax, 9); Assert.Equal(ey, ay, 9); Assert.Equal(ez, az, 9);
            }
            var (nx, ny, nz) = p.Normal;
            Assert.Equal(0, nx, 9); Assert.Equal(-0.5, ny, 9); Assert.Equal(Math.Sqrt(3) / 2, nz, 9);
            // and back: level again, exactly where it was
            var q = p.Then(RotX(-30));
            Assert.False(q.Tilted);
            Assert.Equal(1, q.A00, 9); Assert.Equal(0, q.A01, 9); Assert.Equal(1, q.A11, 9); Assert.Equal(0, q.Ty, 9);
        }

        [Fact]
        public void Plan_rotations_stay_plan_placements()
        {
            double a = 0.7, c = Math.Cos(a), s = Math.Sin(a);
            var p = Placement.Identity.Then(new[,] { { c, -s, 0, 5 }, { s, c, 0, 6 }, { 0, 0, 1, 7 } });
            Assert.False(p.Tilted);
            Assert.Equal(c, p.A00, 12); Assert.Equal(-s, p.A01, 12); Assert.Equal(5, p.Tx, 12); Assert.Equal(7, p.Tz, 12);
        }

        [Fact]
        public void A_field_stood_up_flows_in_its_vertical_plane()
        {
            var f = Uniform(20, 10, 1.0, 0.0, 1.5);                    // flows north
            var g = f.Clone();
            g.Place = f.Place.Then(RotX(90));                           // stood up: north becomes up
            var (lx, ly, lz) = g.Place.Apply(5, 4, 0);
            Assert.Equal(5, lx, 9); Assert.Equal(0, ly, 9); Assert.Equal(4, lz, 9);
            Assert.True(g.SampleWorld(5, 0.3, 4, out var s));          // 0.3 m in front of the wall
            Assert.Equal(0, s.Vx, 9); Assert.Equal(0, s.Vy, 9); Assert.Equal(1.5, s.Vz, 9);
            Assert.Equal(0, s.Gy, 9); Assert.Equal(4, s.Z, 9);           // its ground point is on the wall
            Assert.False(g.SampleWorld(5, 0, 14, out _));               // above the field
            var (bx0, by0, bz0, bx1, by1, bz1) = g.WorldBox();
            Assert.Equal(20, bx1 - bx0, 6); Assert.Equal(0, by1 - by0, 6); Assert.Equal(10, bz1 - bz0, 6);
        }

        [Fact]
        public void Particles_on_a_tilted_field_stay_on_it_at_the_field_speed()
        {
            var f = Uniform(40, 30, 0.5, 1.0, 0.6, z: 0.5f);
            f.Place = f.Place.Then(RotX(40, 5, 0));
            var (nx, ny, nz) = f.Place.Normal;
            var ps = new ParticleSystem(f) { Rate = 150, Life = 30, Seed = 3 };
            ps.StepTo(6);
            Assert.True(ps.Alive.Count > 100);
            double speed = Math.Sqrt(1.0 + 0.36);
            var (ox, oy, oz) = f.Place.Apply(0, 0, 0.5);                // a point on its ground
            foreach (var p in ps.Alive)
            {
                Assert.Equal(speed, p.Speed, 6);
                Assert.Equal(0, (p.X - ox) * nx + (p.Y - oy) * ny + (p.Z - oz) * nz, 6);   // on the plane
                Assert.True(f.SampleWorld(p.X, p.Y, p.Z, out _));
            }
        }

        [Fact]
        public void Streamlines_on_a_tilted_field_lie_in_it_along_the_flow()
        {
            var f = Uniform(40, 30, 0.5, 1.2, 0.0);
            var r = RotX(60);
            var g = f.Clone();
            g.Place = f.Place.Then(r);
            var flat = Streamlines.Trace(f, 1.5);
            var tilted = Streamlines.Trace(g, 1.5);
            Assert.Equal(flat.Count, tilted.Count);
            for (int n = 0; n < flat.Count; n++)
                for (int k = 0; k < flat[n].Count; k++)
                {
                    var (ex, ey, ez) = Mul(r, flat[n].X[k], flat[n].Y[k], flat[n].Z[k]);
                    Assert.Equal(ex, tilted[n].X[k], 9); Assert.Equal(ey, tilted[n].Y[k], 9); Assert.Equal(ez, tilted[n].Z[k], 9);
                }
        }

        [Fact]
        public void Blending_a_tilted_field_with_itself_gives_it_back()
        {
            var f = Uniform(12, 10, 1.0, 1.0, 0.5);
            f.Place = f.Place.Then(RotX(35));
            var b = Blend.Mix(new[] { f, f }, new[] { 1.0, 0.0 });
            Assert.True(b.Place.Tilted);
            Assert.True(b.SampleWorld(f.Place.Apply(4, 5, 0).X, f.Place.Apply(4, 5, 0).Y, f.Place.Apply(4, 5, 0).Z, out var s));
            Assert.True(f.SampleWorld(f.Place.Apply(4, 5, 0).X, f.Place.Apply(4, 5, 0).Y, f.Place.Apply(4, 5, 0).Z, out var t));
            Assert.Equal(t.Vx, s.Vx, 6); Assert.Equal(t.Vy, s.Vy, 6); Assert.Equal(t.Vz, s.Vz, 6);
            // a flat and a tilted one: the tilted one is sampled across its plane
            var flat = Uniform(12, 10, 1.0, 0.0, 1.0);
            var m = Blend.Mix(new[] { flat, f }, new[] { 0.0, 1.0 });
            Assert.True(m.Valid > 0);
        }
    }
}
