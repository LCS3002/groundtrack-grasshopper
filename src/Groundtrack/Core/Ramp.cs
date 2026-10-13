using System;

namespace Groundtrack.Core
{
    /// <summary>Speed colours of the groundtrack maps: blue (slow) to red (fast), each group on
    /// its own scale (people 0-2.5 m/s, vehicles 0-15 or 0-35 m/s ...).</summary>
    public static class Ramp
    {
        // sRGB stops: the same ramp as the groundtrack images, videos and Houdini scripts
        static readonly double[][] Stops =
        {
            new[] { 0.0, 0.2697, 0.3488, 0.7963 }, new[] { 0.1, 0.2659, 0.5598, 0.9958 },
            new[] { 0.2, 0.1270, 0.7804, 0.8759 }, new[] { 0.3, 0.1353, 0.9220, 0.6656 },
            new[] { 0.4, 0.4278, 0.9942, 0.3857 }, new[] { 0.5, 0.7055, 0.9726, 0.2103 },
            new[] { 0.6, 0.9061, 0.8434, 0.2219 }, new[] { 0.7, 0.9953, 0.6534, 0.1958 },
            new[] { 0.8, 0.9619, 0.4109, 0.0931 }, new[] { 0.9, 0.8393, 0.2065, 0.0231 },
            new[] { 1.0, 0.6308, 0.0687, 0.0040 },
        };

        /// <summary>RGB 0..255 for t in 0..1.</summary>
        public static (byte R, byte G, byte B) At(double t)
        {
            if (double.IsNaN(t)) t = 0;
            t = Math.Max(0, Math.Min(1, t));
            for (int k = 1; k < Stops.Length; k++)
            {
                if (t <= Stops[k][0] || k == Stops.Length - 1)
                {
                    var a = Stops[k - 1];
                    var b = Stops[k];
                    double f = (t - a[0]) / Math.Max(b[0] - a[0], 1e-9);
                    f = Math.Max(0, Math.Min(1, f));
                    return (B8(a[1] + (b[1] - a[1]) * f), B8(a[2] + (b[2] - a[2]) * f),
                            B8(a[3] + (b[3] - a[3]) * f));
                }
            }
            return (0, 0, 0);
        }

        /// <summary>RGB for a speed on a group's scale (lo..hi m/s).</summary>
        public static (byte R, byte G, byte B) Speed(double speed, double hi, double lo = 0)
            => At((speed - lo) / Math.Max(hi - lo, 1e-9));

        /// <summary>Inferno-like ramp (black - purple - orange - yellow) for heat maps.</summary>
        public static (byte R, byte G, byte B) Heat(double t)
        {
            if (double.IsNaN(t)) t = 0;
            t = Math.Max(0, Math.Min(1, t));
            double[][] s =
            {
                new[] { 0.00, 0.001, 0.000, 0.014 }, new[] { 0.25, 0.341, 0.063, 0.431 },
                new[] { 0.50, 0.735, 0.216, 0.330 }, new[] { 0.75, 0.978, 0.557, 0.035 },
                new[] { 1.00, 0.988, 1.000, 0.645 },
            };
            for (int k = 1; k < s.Length; k++)
            {
                if (t <= s[k][0] || k == s.Length - 1)
                {
                    double f = (t - s[k - 1][0]) / (s[k][0] - s[k - 1][0]);
                    return (B8(s[k - 1][1] + (s[k][1] - s[k - 1][1]) * f),
                            B8(s[k - 1][2] + (s[k][2] - s[k - 1][2]) * f),
                            B8(s[k - 1][3] + (s[k][3] - s[k - 1][3]) * f));
                }
            }
            return (0, 0, 0);
        }

        static byte B8(double v) => (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);
    }
}
