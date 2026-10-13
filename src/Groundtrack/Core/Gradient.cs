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

        public static readonly string[] Presets = { "groundtrack", "inferno", "viridis", "magma", "greys", "white-pink", "blue-red", "mono" };

        public static Gradient Preset(string name)
        {
            (double, double, double, double)[] s;
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
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
