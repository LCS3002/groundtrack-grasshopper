using System;
using System.Collections.Generic;
using System.Linq;

namespace Groundtrack.Core
{
    /// <summary>A colour gradient for t in 0..1: presets or your own colours (evenly spaced or at
    /// given positions). The default is the groundtrack speed ramp.</summary>
    public sealed class Gradient
    {
        public readonly (double T, double R, double G, double B)[] Stops;   // RGB 0..255
        public string Name = "custom";

        public Gradient(IEnumerable<(double T, double R, double G, double B)> stops)
        {
            Stops = stops.OrderBy(s => s.T).ToArray();
            if (Stops.Length == 0) Stops = new[] { (0.0, 0.0, 0.0, 0.0), (1.0, 255.0, 255.0, 255.0) };
            if (Stops.Length == 1) Stops = new[] { Stops[0], (1.0, Stops[0].R, Stops[0].G, Stops[0].B) };
        }

        /// <summary>Colours (RGB 0..255) evenly spaced, or at positions (0..1).</summary>
        public static Gradient FromColours(IList<(byte R, byte G, byte B)> colours, IList<double> positions = null)
        {
            int n = colours.Count;
            var stops = new List<(double, double, double, double)>();
            for (int k = 0; k < n; k++)
            {
                double t = positions != null && k < positions.Count ? positions[k] : n == 1 ? 0 : k / (double)(n - 1);
                stops.Add((t, colours[k].R, colours[k].G, colours[k].B));
            }
            return new Gradient(stops);
        }

        public static readonly string[] Presets = { "groundtrack", "vivid", "inferno", "viridis", "magma", "greys", "white-pink", "blue-red", "mono" };

        public static Gradient Preset(string name)
        {
            (double, double, double, double)[] s;
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "vivid":         // the groundtrack order (slow blue ... fast red), at full saturation
                    s = new[] { (0.0, 20.0, 40.0, 255.0), (0.2, 0.0, 170.0, 255.0), (0.4, 0.0, 240.0, 120.0), (0.6, 200.0, 255.0, 0.0),
                                (0.8, 255.0, 120.0, 0.0), (1.0, 235.0, 0.0, 40.0) };
                    break;
                case "inferno":
                    s = new[] { (0.0, 0.0, 0.0, 4.0), (0.25, 87.0, 16.0, 110.0), (0.5, 188.0, 55.0, 84.0), (0.75, 249.0, 142.0, 9.0), (1.0, 252.0, 255.0, 164.0) };
                    break;
                case "viridis":
                    s = new[] { (0.0, 68.0, 1.0, 84.0), (0.25, 59.0, 82.0, 139.0), (0.5, 33.0, 145.0, 140.0), (0.75, 94.0, 201.0, 98.0), (1.0, 253.0, 231.0, 37.0) };
                    break;
                case "magma":
                    s = new[] { (0.0, 0.0, 0.0, 4.0), (0.25, 81.0, 18.0, 124.0), (0.5, 183.0, 55.0, 121.0), (0.75, 252.0, 137.0, 97.0), (1.0, 252.0, 253.0, 191.0) };
                    break;
                case "greys":
                    s = new[] { (0.0, 40.0, 40.0, 44.0), (1.0, 245.0, 245.0, 245.0) };
                    break;
                case "white-pink":
                    s = new[] { (0.0, 255.0, 255.0, 255.0), (1.0, 255.0, 40.0, 200.0) };
                    break;
                case "blue-red":
                    s = new[] { (0.0, 40.0, 90.0, 220.0), (0.5, 240.0, 240.0, 240.0), (1.0, 220.0, 40.0, 40.0) };
                    break;
                case "mono":
                    s = new[] { (0.0, 255.0, 255.0, 255.0), (1.0, 255.0, 255.0, 255.0) };
                    break;
                default:
                    s = Enumerable.Range(0, 11).Select(k =>
                    {
                        var (r, g, b) = Ramp.At(k / 10.0);
                        return (k / 10.0, (double)r, (double)g, (double)b);
                    }).ToArray();
                    name = "groundtrack";
                    break;
            }
            return new Gradient(s) { Name = name.ToLowerInvariant() };
        }

        public Gradient Reversed() => new Gradient(Stops.Select(s => (1 - s.T, s.R, s.G, s.B))) { Name = Name + " reversed" };

        /// <summary>The same gradient with saturation x `saturation` and brightness x `brightness`
        /// (HSV; 1 = unchanged): punchier colours for exported images.</summary>
        public Gradient Adjusted(double saturation, double brightness)
        {
            if (Math.Abs(saturation - 1) < 1e-9 && Math.Abs(brightness - 1) < 1e-9) return this;
            return new Gradient(Stops.Select(st =>
            {
                double r = st.R / 255, g = st.G / 255, b = st.B / 255;
                double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), v = max, d = max - min;
                double sat = max > 1e-12 ? d / max : 0, h = 0;
                if (d > 1e-12)
                {
                    if (max == r) h = ((g - b) / d + 6) % 6;
                    else if (max == g) h = (b - r) / d + 2;
                    else h = (r - g) / d + 4;
                }
                sat = Math.Max(0, Math.Min(1, sat * saturation));
                v = Math.Max(0, Math.Min(1, v * brightness));
                double c = v * sat, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
                (double R, double G, double B) rgb = h < 1 ? (c, x, 0) : h < 2 ? (x, c, 0) : h < 3 ? (0, c, x) : h < 4 ? (0, x, c) : h < 5 ? (x, 0, c) : (c, 0, x);
                return (st.T, (rgb.R + m) * 255, (rgb.G + m) * 255, (rgb.B + m) * 255);
            })) { Name = Name };
        }

        public (byte R, byte G, byte B) At(double t)
        {
            if (double.IsNaN(t)) t = 0;
            t = Math.Max(0, Math.Min(1, t));
            if (t <= Stops[0].T) return B(Stops[0]);
            for (int k = 1; k < Stops.Length; k++)
            {
                if (t <= Stops[k].T)
                {
                    var a = Stops[k - 1];
                    var b = Stops[k];
                    double f = (t - a.T) / Math.Max(b.T - a.T, 1e-12);
                    return ((byte)Math.Round(a.R + (b.R - a.R) * f), (byte)Math.Round(a.G + (b.G - a.G) * f),
                            (byte)Math.Round(a.B + (b.B - a.B) * f));
                }
            }
            return B(Stops[Stops.Length - 1]);
        }

        static (byte, byte, byte) B((double T, double R, double G, double B) s)
            => ((byte)Math.Round(s.R), (byte)Math.Round(s.G), (byte)Math.Round(s.B));
    }
}
