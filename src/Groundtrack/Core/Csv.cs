using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Groundtrack.Core
{
    /// <summary>A plain CSV reader for groundtrack's files (comma separated, no quoting).</summary>
    public sealed class Csv
    {
        public readonly string[] Header;
        public readonly List<string[]> Rows = new List<string[]>();
        readonly Dictionary<string, int> _col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public Csv(string path)
        {
            using (var r = new StreamReader(path))
            {
                Header = (r.ReadLine() ?? "").Split(',');
                for (int k = 0; k < Header.Length; k++) _col[Header[k].Trim()] = k;
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length > 0) Rows.Add(line.Split(','));
                }
            }
        }

        public bool Has(string name) => _col.ContainsKey(name);
        public int Col(string name) => _col.TryGetValue(name, out int k) ? k : -1;

        public static double D(string[] row, int col, double missing = double.NaN)
        {
            if (col < 0 || col >= row.Length || row[col].Length == 0) return missing;
            return double.TryParse(row[col], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : missing;
        }

        public static bool B(string[] row, int col)
        {
            if (col < 0 || col >= row.Length) return false;
            string s = row[col].Trim();
            return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public static string S(string[] row, int col) => col >= 0 && col < row.Length ? row[col] : "";
    }
}
