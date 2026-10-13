using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Groundtrack.Core;
using Xunit;

namespace Groundtrack.Tests
{
    public class CoreTests
    {
        /// <summary>A field of nx x ny cells of `cell` m, all moving with (vx, vy).</summary>
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
        public void Ramp_runs_blue_to_red()
        {
            var lo = Ramp.At(0);
            var hi = Ramp.At(1);
            Assert.True(lo.B > lo.R);
            Assert.True(hi.R > hi.B);
            Assert.Equal(Ramp.At(0.5), Ramp.Speed(1.25, 2.5));
        }

        [Fact]
        public void Sample_is_exact_at_cells_bilinear_between_and_false_outside()
        {
            var f = Uniform(4, 3, 1.0, 0, 0);
            f.Vx[f.Nx * 1 + 1] = 2;                       // one cell moves east
            Assert.True(f.Sample(1, 1, out var s));
            Assert.Equal(2, s.Vx, 6);
            Assert.True(f.Sample(1.5, 1, out s));
            Assert.Equal(1, s.Vx, 6);                     // half way to its neighbour
            Assert.False(f.Sample(-2, 1, out s));
            Assert.True(f.Sample(-0.4, 1, out s));        // half a cell out: still the edge
        }

        [Fact]
        public void Placement_turns_and_moves_the_field_and_its_velocities()
        {
            var f = Uniform(5, 5, 1.0, 1.0, 0.0);
            var g = f.Clone();
            g.Place = new Placement(0, -1, 1, 0, 100, 50, 1, 2);   // 90 degrees, then moved
            Assert.True(g.Sample(100 - 2, 50 + 3, out var s));    // local (3, 2)
            Assert.Equal(0, s.Vx, 6);
            Assert.Equal(1, s.Vy, 6);                              // east turned to north
            Assert.Equal(2, s.Z, 6);
            Assert.False(g.Sample(3, 2, out _));
        }

        [Fact]
        public void Streamlines_follow_the_flow_and_keep_their_spacing()
        {
            var f = Uniform(40, 30, 0.5, 1.2, 0.0);
            var lines = Streamlines.Trace(f, 1.5);
            Assert.True(lines.Count >= 8);
            foreach (var l in lines)
            {
                for (int k = 1; k < l.Count; k++)
                    Assert.True(Math.Abs(l.Y[k] - l.Y[k - 1]) < 1e-6);   // straight along x
                Assert.True(l.Length > 10);
            }
            var ys = lines.Select(l => l.Y[0]).OrderBy(y => y).ToList();
            for (int k = 1; k < ys.Count; k++) Assert.True(ys[k] - ys[k - 1] >= 0.75 - 1e-9);
        }

        [Fact]
        public void Particles_move_at_the_field_speed_stay_inside_and_replay_exactly()
        {
            var f = Uniform(60, 20, 0.5, 1.3, 0.4);
            var ps = new ParticleSystem(f) { Rate = 100, Life = 30, Seed = 3 };
            ps.StepTo(5);
            Assert.True(ps.Alive.Count > 100);
            double speed = Math.Sqrt(1.3 * 1.3 + 0.4 * 0.4);
            foreach (var p in ps.Alive)
            {
                Assert.Equal(speed, p.Speed, 6);
                Assert.True(f.Sample(p.X, p.Y, out _));
            }
            var at5 = ps.Alive.Select(p => (p.Id, p.X, p.Y)).ToList();
            ps.StepTo(8);
            ps.StepTo(5);                                 // back: re-run from 0
            Assert.Equal(at5, ps.Alive.Select(p => (p.Id, p.X, p.Y)).ToList());
        }

        [Fact]
        public void Blend_gives_its_inputs_back_at_0_and_1()
        {
            var a = Uniform(10, 8, 1.0, 1, 0);
            var b = Uniform(6, 6, 1.0, 0, 2, 4, 3);
            for (int k = 0; k < a.Count; k++) a.Density[k] = 0.3f;
            foreach (var (w, src) in new[] { (new[] { 1.0, 0.0 }, a), (new[] { 0.0, 1.0 }, b) })
            {
                var m = Blend.Mix(new[] { a, b }, w);
                for (int k = 0; k < src.Count; k++)
                {
                    var (x, y, _) = src.CellWorld(k);
                    Assert.True(m.Sample(x, y, out var s));
                    Assert.True(src.Sample(x, y, out var e));
                    Assert.Equal(e.Vx, s.Vx, 6); Assert.Equal(e.Vy, s.Vy, 6);
                    Assert.Equal(e.Confidence, s.Confidence, 6); Assert.Equal(e.Density, s.Density, 6);
                    Assert.Equal(e.MeanSpeed, s.MeanSpeed, 6);
                }
            }
            var half = Blend.Mix(new[] { a, b }, new[] { 0.5, 0.5 });
            Assert.True(half.Sample(6, 5, out var o));        // both fields there: the average
            Assert.Equal(0.5, o.Vx, 6); Assert.Equal(1.0, o.Vy, 6);
        }

        [Fact]
        public void Crossings_counts_every_crossing_once_with_its_angle()
        {
            var hor = Enumerable.Range(0, 5).Select(i => (IList<(double, double, double)>)new List<(double, double, double)>
                { (0, i + 0.5, 0), (5, i + 0.5, 0), (10, i + 0.5, 0) }).ToList();
            var ver = Enumerable.Range(0, 7).Select(i => (IList<(double, double, double)>)new List<(double, double, double)>
                { (i + 1.25, -1, 0), (i + 1.25, 7, 0) }).ToList();
            var hits = Crossings.Find(hor, ver);
            Assert.Equal(35, hits.Count);
            Assert.All(hits, h => Assert.Equal(90, h.Angle, 6));
            var all = hor.Concat(ver).ToList();
            Assert.Equal(35, Crossings.Find(all).Count);       // self mode: line pairs once
            var heat = Crossings.Map(hits);
            Assert.Equal(1.0, heat.Value.Max(), 6);
        }

        [Fact]
        public void Catalog_reads_a_results_folder_into_the_site_frame()
        {
            string root = Path.Combine(Path.GetTempPath(), "gt_results_" + Guid.NewGuid().ToString("N"));
            string h = Path.Combine(root, "01_test_site", "composite", "houdini");
            Directory.CreateDirectory(h);
            File.WriteAllText(Path.Combine(h, "houdini_field.py"),
                "ORIGIN = (1000.0, 2000.0)\nSPEED_RANGES = {'people': (0.0, 2.5), 'vehicles': (0.0, 15.0), 'all': (0.0, 15.0)}\n");
            var rows = new List<string> { "i,j,cell_x,cell_y,vx,vy,speed,heading,density,weight,confidence,dominance" };
            for (int i = 2000; i < 2010; i++)
                for (int j = 4000; j < 4004; j++)
                    rows.Add(FormattableString.Invariant($"{i},{j},{(i + 0.5) * 0.5},{(j + 0.5) * 0.5},1.0,0.5,1.2,63,0.01,1,0.8,1"));
            File.WriteAllLines(Path.Combine(h, "vector_field_people_continuous.csv"), rows);
            var pts = new List<string> { "track_id,class,frame,time_s,x,y,vx,vy,speed,heading,predicted,source,group,beyond_limit,z" };
            for (int k = 0; k < 30; k++)
                pts.Add(FormattableString.Invariant($"1,person,{k},{k / 10.0},{1000.2 + k * 0.1},{2000.2},1.0,0.0,1.0,90,False,detected,people,False,3.0"));
            File.WriteAllLines(Path.Combine(h, "points.csv"), pts);
            File.AppendAllText(Path.Combine(h, "houdini_field.py"), "Z0 = 1.0\n");

            var sites = Catalog.Sites(root);
            Assert.Single(sites);
            Assert.Equal("test_site", sites[0].Name);
            Assert.Equal(new[] { "whole" }, sites[0].Clips);
            var f = Catalog.Load(sites[0], "whole", "people", true);
            Assert.Equal(0.5, f.Cell, 9);
            Assert.Equal(10, f.Nx); Assert.Equal(4, f.Ny);
            Assert.Equal(0.25, f.X0, 6);                       // (2000.5 * 0.5) - 1000
            Assert.Equal(2.5, f.Smax[0], 6);
            Assert.Equal(2.0, f.Z[0], 6);                      // 3.0 above sea level - Z0 1.0
            var t = Catalog.Tracks(f, sites[0]);
            Assert.Equal(1, t.Tracks);
            var at = t.At(1.55);
            Assert.Single(at);
            Assert.Equal(0.2 + 1.55, at[0].X, 4);             // moved 1 m/s
            var slice = Catalog.FromTracks(f, t, 0, 1.0, "people");
            Assert.True(slice.Valid > 0 && slice.Valid < f.Valid);
            Directory.Delete(root, true);
        }
    }
}
