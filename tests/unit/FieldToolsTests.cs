using System;
using System.Linq;
using Groundtrack.Core;
using Xunit;

namespace Groundtrack.Tests
{
    public class FieldToolsTests
    {
        static FieldData Uniform(int nx, int ny, double cell, double vx, double vy, double x0 = 0, double y0 = 0, float z = 0)
        {
            var f = FieldData.Empty(nx, ny, cell, x0, y0);
            for (int k = 0; k < f.Count; k++)
            {
                f.Has[k] = true; f.Vx[k] = (float)vx; f.Vy[k] = (float)vy; f.Z[k] = z;
                f.Speed[k] = (float)Math.Sqrt(vx * vx + vy * vy); f.Density[k] = 1;
                f.Confidence[k] = 1; f.Smax[k] = 2.5f;
            }
            return f;
        }

        [Fact]
        public void Smoothing_keeps_a_uniform_flow_and_ramps_a_step()
        {
            var f = Uniform(60, 20, 0.5, 1.0, 0.0);
            var s = FieldTools.Smooth(f, 3);
            Assert.Equal(f.Valid, s.Valid);
            Assert.True(s.Sample(15, 5, out var a));
            Assert.Equal(1.0, a.Vx, 5);
            for (int j = 0; j < f.Ny; j++)            // a hard step: the right half goes north
                for (int i = 30; i < f.Nx; i++)
                {
                    int k = j * f.Nx + i;
                    f.Vx[k] = 0; f.Vy[k] = 1;
                }
            s = FieldTools.Smooth(f, 3);
            double jumpHard = 0, jumpSmooth = 0;
            for (int i = 1; i < f.Nx; i++)
            {
                int k = 10 * f.Nx + i;
                jumpHard = Math.Max(jumpHard, Math.Abs(f.Vy[k] - f.Vy[k - 1]));
                jumpSmooth = Math.Max(jumpSmooth, Math.Abs(s.Vy[k] - s.Vy[k - 1]));
            }
            Assert.Equal(1.0, jumpHard, 6);
            Assert.True(jumpSmooth < 0.3, $"largest step after smoothing {jumpSmooth:0.00}");
        }

        [Fact]
        public void Fill_closes_a_gap()
        {
            var f = Uniform(40, 10, 0.5, 1.0, 0.0);
            for (int j = 0; j < f.Ny; j++)
                for (int i = 18; i < 22; i++) f.Has[j * f.Nx + i] = false;      // a 2 m gap
            var s = FieldTools.Smooth(f, 3, fill: true);
            Assert.Equal(f.Count, s.Valid);
            Assert.True(s.Sample(10, 2.5, out var a));
            Assert.Equal(1.0, a.Vx, 3);
        }

        [Fact]
        public void Feathered_merge_ramps_between_heights_instead_of_jumping()
        {
            var a = Uniform(40, 40, 0.5, 1, 0, 0, 0, 0);       // x 0..20 at z 0 (20 m wide)
            var b = Uniform(40, 40, 0.5, 1, 0, 10, 0, 10);     // x 10..30 at z 10
            double MaxJump(FieldData m)
            {
                double jump = 0;
                for (double x = 0; x < 29.5; x += 0.5)
                {
                    if (!m.Sample(x, 10, out var p) || !m.Sample(x + 0.5, 10, out var q)) continue;
                    jump = Math.Max(jump, Math.Abs(q.Z - p.Z));
                }
                return jump;
            }
            double hard = MaxJump(Blend.Merge(new[] { a, b }));
            double soft = MaxJump(Blend.Merge(new[] { a, b }, feather: 4));
            double softer = MaxJump(Blend.Merge(new[] { a, b }, feather: 4, smooth: 1.5));
            Assert.True(hard >= 4.9, $"hard merge jumps {hard:0.0} m");
            // 10 m of height over a 4 m ramp: steps of a 0.5 m cell are ~1 m at the steepest
            Assert.True(soft <= 1.1, $"feathered merge steps {soft:0.00} m");
            Assert.True(softer <= 0.9, $"feathered + smoothed merge steps {softer:0.00} m");
        }

        [Fact]
        public void Slope_limit_turns_cliffs_into_ramps_and_leaves_gentle_ground()
        {
            var f = Uniform(80, 20, 0.5, 1, 0);
            for (int j = 0; j < f.Ny; j++)
                for (int i = 0; i < f.Nx; i++)
                    f.Z[j * f.Nx + i] = i < 40 ? (float)(0.1 * i * 0.5) : 10f;      // a gentle slope, then a cliff
            var o = FieldTools.LimitSlope(f, 1.0);
            double steepest = 0;
            for (int j = 0; j < f.Ny; j++)
                for (int i = 1; i < f.Nx; i++)
                    steepest = Math.Max(steepest, Math.Abs(o.Z[j * f.Nx + i] - o.Z[j * f.Nx + i - 1]) / 0.5);
            Assert.True(steepest <= 1.0 + 1e-9, $"steepest {steepest:0.00}");
            Assert.Equal(f.Z[10 * f.Nx + 5], o.Z[10 * f.Nx + 5], 5);              // far from the cliff: unchanged
            Assert.Equal(10f, o.Z[10 * f.Nx + 79], 5);
        }
    }
}
