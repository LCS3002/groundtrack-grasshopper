using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

namespace Groundtrack
{
    internal static class Icons
    {
        static readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(string name)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(name, out var b)) return b;
                var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Groundtrack.Icons.{name}.png");
                b = s == null ? null : new Bitmap(s);
                _cache[name] = b;
                return b;
            }
        }
    }
}
