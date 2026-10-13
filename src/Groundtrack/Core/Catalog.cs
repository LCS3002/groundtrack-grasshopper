using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Groundtrack.Core
{
    /// <summary>A site of a groundtrack results folder (a folder with composite/houdini/).</summary>
    public sealed class SiteInfo
    {
        public string Name = "", Folder = "";
        public double OriginE, OriginN, Z0;
        public Dictionary<string, double> Top = new Dictionary<string, double>();   // group -> top speed
        public List<string> Clips = new List<string>();                             // "whole" first

        public double TopOf(string group)
            => Top.TryGetValue(group ?? "", out double v) ? v
               : Top.TryGetValue("all", out double a) ? a : 2.5;
    }

    /// <summary>Finds sites, clips, groups and fields in a groundtrack results folder and reads
    /// them into the site's frame (metres around the site origin, Easting -> +X, Northing -> +Y).</summary>
    public static class Catalog
    {
        public const string Whole = "whole";
        public static readonly string[] GroupOrder = { "all", "people", "cycles", "vehicles", "buses", "trains" };

        static readonly ConcurrentDictionary<string, (DateTime, List<SiteInfo>)> _sites =
            new ConcurrentDictionary<string, (DateTime, List<SiteInfo>)>();
        static readonly ConcurrentDictionary<string, (DateTime, FieldData)> _fields =
            new ConcurrentDictionary<string, (DateTime, FieldData)>();

        /// <summary>The results folder: the GROUNDTRACK_RESULTS environment variable, else a
        /// folder called "GROUNDTRACK RESULTS" under Documents (searched a few levels deep).</summary>
        public static string FindRoot()
        {
            string env = Environment.GetEnvironmentVariable("GROUNDTRACK_RESULTS");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Search(docs, 6) ?? "";
        }

        static string Search(string dir, int depth)
        {
            if (depth < 0 || !Directory.Exists(dir)) return null;
            try
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    string n = Path.GetFileName(d);
                    if (n.Equals("GROUNDTRACK RESULTS", StringComparison.OrdinalIgnoreCase)) return d;
                }
                foreach (var d in Directory.GetDirectories(dir))
                {
                    string n = Path.GetFileName(d);
                    if (n.StartsWith(".") || n.StartsWith("$") || n.Equals("node_modules")) continue;
                    var hit = Search(d, depth - 1);
                    if (hit != null) return hit;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            return null;
        }

        public static List<SiteInfo> Sites(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return new List<SiteInfo>();
            var stamp = Directory.GetLastWriteTimeUtc(root);
            if (_sites.TryGetValue(root, out var c) && c.Item1 == stamp) return c.Item2;
            var list = new List<SiteInfo>();
            foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
            {
                string script = Path.Combine(dir, "composite", "houdini", "houdini_field.py");
                if (!File.Exists(script)) continue;
                var s = new SiteInfo { Folder = dir, Name = SiteName(Path.GetFileName(dir)) };
                ReadScript(script, s);
                string gh = Path.Combine(dir, "composite", "rhino", "grasshopper_import.py");
                if (File.Exists(gh)) ReadScript(gh, s);      // the Rhino files' origin + ground
                s.Clips.Add(Whole);
                string clips = Path.Combine(dir, "clips");
                if (Directory.Exists(clips))
                    foreach (var cd in Directory.GetDirectories(clips).OrderBy(d => d))
                        if (FieldFiles(s, Path.GetFileName(cd)).Count > 0) s.Clips.Add(Path.GetFileName(cd));
                list.Add(s);
            }
            _sites[root] = (stamp, list);
            return list;
        }

        /// <summary>"01_trafalgar_square" -> "trafalgar_square".</summary>
        public static string SiteName(string folder) => Regex.Replace(folder, @"^\d+[_ -]+", "");

        static void ReadScript(string path, SiteInfo s)
        {
            string txt = File.ReadAllText(path);
            var m = Regex.Match(txt, @"^ORIGIN = \(\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)", RegexOptions.Multiline);
            if (m.Success)
            {
                s.OriginE = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                s.OriginN = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            m = Regex.Match(txt, @"^Z0 = (-?[\d.]+)", RegexOptions.Multiline);
            if (m.Success) s.Z0 = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            m = Regex.Match(txt, @"^SPEED_RANGES = \{(.*)\}", RegexOptions.Multiline);
            if (m.Success)
                foreach (Match g in Regex.Matches(m.Groups[1].Value, @"'(\w+)':\s*\(\s*([\d.]+)\s*,\s*([\d.]+)\s*\)"))
                    s.Top[g.Groups[1].Value] = double.Parse(g.Groups[3].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>group -> field csv of a clip ("whole" = the combined plan of the site).</summary>
        public static Dictionary<string, string> FieldFiles(SiteInfo s, string clip)
        {
            var d = new Dictionary<string, string>();
            if (clip == Whole)
            {
                string h = Path.Combine(s.Folder, "composite", "houdini");
                if (File.Exists(Path.Combine(h, "vector_field_master.csv")))
                    d["all"] = Path.Combine(h, "vector_field_master.csv");
                foreach (var g in GroupOrder)
                {
                    string f = Path.Combine(h, $"vector_field_{g}_continuous.csv");
                    if (File.Exists(f)) d[g] = f;
                }
                if (d.Count == 0) Plain(h, d);
            }
            else Plain(Path.Combine(s.Folder, "clips", clip, "houdini"), d);
            return GroupOrder.Where(d.ContainsKey).ToDictionary(g => g, g => d[g]);
        }

        static void Plain(string dir, Dictionary<string, string> d)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var g in GroupOrder)
            {
                string f = Path.Combine(dir, $"vector_field_{g}.csv");
                if (File.Exists(f)) d[g] = f;
            }
            // "all" mixes the groups: offered when there are several; a clip with one group
            // only has vector_field.csv, named after that group (from its houdini script)
            string all = Path.Combine(dir, "vector_field.csv");
            if (!File.Exists(all) || d.Count == 1) return;
            if (d.Count == 0)
            {
                var groups = GroupsIn(Path.Combine(dir, "points.csv"));
                if (groups.Count == 1) { d[groups[0]] = all; return; }
            }
            d["all"] = all;
        }

        /// <summary>The groups in a tracks file (read from its first few thousand rows).</summary>
        static List<string> GroupsIn(string points)
        {
            var res = new List<string>();
            if (!File.Exists(points)) return res;
            using (var r = new StreamReader(points))
            {
                var head = (r.ReadLine() ?? "").Split(',');
                int c = Array.IndexOf(head, "group");
                if (c < 0) return res;
                string line;
                for (int n = 0; n < 5000 && (line = r.ReadLine()) != null; n++)
                {
                    var parts = line.Split(',');
                    if (c < parts.Length && parts[c].Length > 0 && !res.Contains(parts[c])) res.Add(parts[c]);
                }
            }
            return res;
        }

        public static string TracksPath(SiteInfo s, string clip)
            => clip == Whole ? Path.Combine(s.Folder, "composite", "houdini", "points.csv")
                             : Path.Combine(s.Folder, "clips", clip, "houdini", "points.csv");

        /// <summary>A site's field (cached until the file changes).</summary>
        public static FieldData Load(SiteInfo s, string clip, string group, bool heights)
        {
            var files = FieldFiles(s, clip);
            if (files.Count == 0) throw new FileNotFoundException($"no field files for {s.Name} / {clip}");
            if (!files.TryGetValue(group ?? "", out string path))
            {
                group = files.Keys.First();
                path = files[group];
            }
            string tracks = TracksPath(s, clip);
            string key = $"{path}|{heights}";
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_fields.TryGetValue(key, out var c) && c.Item1 == stamp) return c.Item2;
            var f = Read(path, s, group);
            f.Site = s.Name; f.Clip = clip; f.Group = group; f.Source = path;
            foreach (var kv in s.Top) f.Tops[kv.Key] = kv.Value;
            f.TracksPath = File.Exists(tracks) ? tracks : "";
            if (heights && f.TracksPath.Length > 0) AddHeights(f, Tracks(f, s));
            _fields[key] = (stamp, f);
            return f;
        }

        /// <summary>The tracks behind a field (read from its folder, cached).</summary>
        public static TrackSet Tracks(FieldData f, SiteInfo s)
            => string.IsNullOrEmpty(f.TracksPath) ? null
               : TrackSet.Load(f.TracksPath, s.OriginE, s.OriginN, s.Z0, s.Top);

        /// <summary>Read a groundtrack vector field csv into the site's frame.</summary>
        public static FieldData Read(string path, SiteInfo s, string group)
        {
            var csv = new Csv(path);
            int ci = csv.Col("i"), cj = csv.Col("j"), cx = csv.Col("cell_x"), cy = csv.Col("cell_y");
            int cvx = csv.Col("vx"), cvy = csv.Col("vy"), csp = csv.Col("speed"), cd = csv.Col("density");
            int cc = csv.Col("confidence"), cg = csv.Col("dominant_group");
            if (ci < 0 || cj < 0 || cx < 0 || cvx < 0) throw new InvalidDataException($"not a vector field: {path}");
            int n = csv.Rows.Count;
            if (n == 0) throw new InvalidDataException($"empty field: {path}");
            var cells = new List<double>();
            int i0 = int.MaxValue, i1 = int.MinValue, j0 = int.MaxValue, j1 = int.MinValue;
            foreach (var r in csv.Rows)
            {
                int i = (int)Csv.D(r, ci), j = (int)Csv.D(r, cj);
                i0 = Math.Min(i0, i); i1 = Math.Max(i1, i); j0 = Math.Min(j0, j); j1 = Math.Max(j1, j);
                if (cells.Count < 200) cells.Add(Csv.D(r, cx) / (i + 0.5));
            }
            cells.Sort();
            double cell = cells[cells.Count / 2];
            var f = FieldData.Empty(i1 - i0 + 1, j1 - j0 + 1, cell,
                                    (i0 + 0.5) * cell - s.OriginE, (j0 + 0.5) * cell - s.OriginN);
            f.OriginE = s.OriginE; f.OriginN = s.OriginN; f.Z0 = s.Z0;
            double top = s.TopOf(group);
            foreach (var r in csv.Rows)
            {
                int k = ((int)Csv.D(r, cj) - j0) * f.Nx + ((int)Csv.D(r, ci) - i0);
                double vx = Csv.D(r, cvx), vy = Csv.D(r, cvy);
                if (double.IsNaN(vx) || double.IsNaN(vy)) continue;
                f.Has[k] = true;
                f.Vx[k] = (float)vx; f.Vy[k] = (float)vy;
                double sp = Csv.D(r, csp);
                f.Speed[k] = (float)(double.IsNaN(sp) ? Math.Sqrt(vx * vx + vy * vy) : sp);
                f.Density[k] = (float)Math.Max(0, Csv.D(r, cd, 0));
                f.Confidence[k] = (float)Math.Max(0, Math.Min(1, Csv.D(r, cc, 1)));
                f.Smax[k] = (float)(cg >= 0 && Csv.S(r, cg).Length > 0 ? s.TopOf(Csv.S(r, cg)) : top);
            }
            return f;
        }

        /// <summary>Ground height of each cell (median height of the track samples in it, above
        /// the site's ground level); cells without samples take their nearest neighbour's.</summary>
        public static void AddHeights(FieldData f, TrackSet t)
        {
            var lists = new Dictionary<int, List<float>>();
            for (int k = 0; k < t.X.Length; k++)
            {
                if (float.IsNaN(t.Z[k])) continue;
                int i = (int)Math.Round((t.X[k] - f.X0) / f.Cell), j = (int)Math.Round((t.Y[k] - f.Y0) / f.Cell);
                if (i < 0 || j < 0 || i >= f.Nx || j >= f.Ny) continue;
                int c = j * f.Nx + i;
                if (!lists.TryGetValue(c, out var l)) lists[c] = l = new List<float>();
                if (l.Count < 64) l.Add(t.Z[k]);
            }
            if (lists.Count == 0) return;
            var known = new bool[f.Count];
            foreach (var kv in lists)
            {
                kv.Value.Sort();
                f.Z[kv.Key] = kv.Value[kv.Value.Count / 2];
                known[kv.Key] = true;
            }
            // spread to the other cells, ring by ring (breadth first)
            var queue = new Queue<int>(lists.Keys);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int i = c % f.Nx, j = c / f.Nx;
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= f.Nx || nj >= f.Ny) continue;
                    int nc = nj * f.Nx + ni;
                    if (known[nc]) continue;
                    known[nc] = true;
                    f.Z[nc] = f.Z[c];
                    queue.Enqueue(nc);
                }
            }
        }

        /// <summary>A field from the tracks in a time window [t0, t1] on the grid of a template
        /// field: the movement in that moment.</summary>
        public static FieldData FromTracks(FieldData template, TrackSet t, double t0, double t1, string group)
        {
            var f = FieldData.Empty(template.Nx, template.Ny, template.Cell, template.X0, template.Y0);
            f.Site = template.Site; f.Clip = template.Clip; f.Group = template.Group;
            f.Source = template.Source; f.TracksPath = template.TracksPath;
            f.OriginE = template.OriginE; f.OriginN = template.OriginN; f.Z0 = template.Z0;
            f.Place = template.Place; f.TimeStart = t0; f.TimeEnd = t1; f.Tops = template.Tops;
            Array.Copy(template.Z, f.Z, f.Z.Length);
            var n = new int[f.Count];
            int g = group == "all" || string.IsNullOrEmpty(group) ? -1 : Array.IndexOf(t.GroupNames, group);
            if (g < 0 && !(group == "all" || string.IsNullOrEmpty(group))) return f;
            for (int k = 0; k < t.X.Length; k++)
            {
                if (t.T[k] < t0 || t.T[k] > t1 || t.Predicted[k]) continue;
                if (g >= 0 && t.Group[k] != g) continue;
                int i = (int)Math.Round((t.X[k] - f.X0) / f.Cell), j = (int)Math.Round((t.Y[k] - f.Y0) / f.Cell);
                if (i < 0 || j < 0 || i >= f.Nx || j >= f.Ny) continue;
                int c = j * f.Nx + i;
                n[c]++;
                f.Vx[c] += t.Vx[k];
                f.Vy[c] += t.Vy[k];
                f.Smax[c] = Math.Max(f.Smax[c], (float)t.TopOf(t.Group[k]));
            }
            double dt = t.SampleDt;
            float fallback = 2.5f;
            for (int k = 0; k < template.Count; k++)
                if (template.Has[k]) fallback = Math.Max(fallback, template.Smax[k]);
            for (int c = 0; c < f.Count; c++)
            {
                if (n[c] == 0) continue;
                f.Has[c] = true;
                f.Vx[c] /= n[c]; f.Vy[c] /= n[c];
                f.Speed[c] = (float)Math.Sqrt(f.Vx[c] * f.Vx[c] + f.Vy[c] * f.Vy[c]);
                f.Density[c] = (float)(n[c] * dt / (f.Cell * f.Cell));
                f.Confidence[c] = (float)(1 - Math.Exp(-n[c] / 4.0));
                if (f.Smax[c] <= 0) f.Smax[c] = template.Has[c] ? template.Smax[c] : fallback;
            }
            return f;
        }
    }
}
