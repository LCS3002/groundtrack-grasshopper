using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Groundtrack.Core
{
    /// <summary>The tracked samples of a clip or a site (groundtrack points.csv): every person /
    /// vehicle at every frame, in the site frame. Read locally, never bundled.</summary>
    public sealed class TrackSet
    {
        public string FilePath = "";
        public float[] T, X, Y, Z, Vx, Vy, Speed;
        public byte[] Group;
        public bool[] Predicted;
        public string[] GroupNames = Array.Empty<string>();
        public double[] GroupTop = Array.Empty<double>();
        public int[] Start, Len, Id;                // per track: first sample, sample count, id
        public double TMin, TMax, SampleDt = 1 / 30.0;

        public int Tracks => Start.Length;
        public double TopOf(int g) => g >= 0 && g < GroupTop.Length ? GroupTop[g] : 2.5;

        static readonly ConcurrentDictionary<string, (DateTime, TrackSet)> _cache =
            new ConcurrentDictionary<string, (DateTime, TrackSet)>();

        public static TrackSet Load(string path, double originE, double originN, double z0,
                                    IDictionary<string, double> tops = null)
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            string key = $"{path}|{originE}|{originN}|{z0}";
            if (_cache.TryGetValue(key, out var c) && c.Item1 == stamp) return c.Item2;
            var csv = new Csv(path);
            int cid = csv.Col("track_id"), ct = csv.Col("time_s"), cx = csv.Col("x"), cy = csv.Col("y");
            int cz = csv.Col("z"), cvx = csv.Col("vx"), cvy = csv.Col("vy"), cs = csv.Col("speed");
            int cg = csv.Col("group"), cp = csv.Col("predicted"), cclip = csv.Col("clip");
            int n = csv.Rows.Count;
            var ids = new long[n];
            var t = new TrackSet
            {
                FilePath = path, T = new float[n], X = new float[n], Y = new float[n], Z = new float[n],
                Vx = new float[n], Vy = new float[n], Speed = new float[n], Group = new byte[n],
                Predicted = new bool[n],
            };
            var groups = new List<string>();
            var clips = new Dictionary<string, int>();
            var rows = csv.Rows;
            var order = Enumerable.Range(0, n).ToArray();
            var tmp = new double[n];
            for (int k = 0; k < n; k++)
            {
                var r = rows[k];
                // composite files hold several clips: the track id is unique per clip
                long clip = 0;
                if (cclip >= 0)
                {
                    string cn = Csv.S(r, cclip);
                    if (!clips.TryGetValue(cn, out int ix)) clips[cn] = ix = clips.Count;
                    clip = ix;
                }
                ids[k] = clip * 100_000_000L + (long)Csv.D(r, cid, 0);
                tmp[k] = Csv.D(r, ct, 0);
            }
            Array.Sort(order, (a, b) => ids[a] != ids[b] ? ids[a].CompareTo(ids[b]) : tmp[a].CompareTo(tmp[b]));
            var start = new List<int>();
            var len = new List<int>();
            var idl = new List<int>();
            double tmin = double.MaxValue, tmax = double.MinValue;
            var steps = new List<double>();
            for (int o = 0; o < n; o++)
            {
                int k = order[o];
                var r = rows[k];
                t.T[o] = (float)tmp[k];
                t.X[o] = (float)(Csv.D(r, cx) - originE);
                t.Y[o] = (float)(Csv.D(r, cy) - originN);
                double z = Csv.D(r, cz);
                t.Z[o] = (float)(double.IsNaN(z) ? 0 : (z0 != 0 ? z - z0 : 0));
                t.Vx[o] = (float)Csv.D(r, cvx, 0);
                t.Vy[o] = (float)Csv.D(r, cvy, 0);
                t.Speed[o] = (float)Csv.D(r, cs, Math.Sqrt(t.Vx[o] * t.Vx[o] + t.Vy[o] * t.Vy[o]));
                string g = Csv.S(r, cg);
                int gi = groups.IndexOf(g);
                if (gi < 0) { groups.Add(g); gi = groups.Count - 1; }
                t.Group[o] = (byte)gi;
                t.Predicted[o] = Csv.B(r, cp);
                if (o == 0 || ids[k] != ids[order[o - 1]])
                {
                    start.Add(o); len.Add(0); idl.Add((int)(ids[k] % 100_000_000L));
                }
                else if (steps.Count < 2000) steps.Add(t.T[o] - t.T[o - 1]);
                len[len.Count - 1]++;
                tmin = Math.Min(tmin, t.T[o]); tmax = Math.Max(tmax, t.T[o]);
            }
            t.Start = start.ToArray(); t.Len = len.ToArray(); t.Id = idl.ToArray();
            t.TMin = n > 0 ? tmin : 0; t.TMax = n > 0 ? tmax : 0;
            t.GroupNames = groups.ToArray();
            t.GroupTop = groups.Select(g => tops != null && tops.TryGetValue(g, out double v) ? v
                                          : g == "people" ? 2.5 : g == "cycles" ? 10 : 15).ToArray();
            if (steps.Count > 0)
            {
                steps.Sort();
                t.SampleDt = Math.Max(steps[steps.Count / 2], 1e-3);
            }
            _cache[key] = (stamp, t);
            return t;
        }

        /// <summary>Index of the last sample of a track at or before time tm (-1 before it starts).</summary>
        public int SampleAt(int track, double tm)
        {
            int a = Start[track], b = a + Len[track] - 1;
            if (tm < T[a] || tm > T[b]) return -1;
            while (b - a > 1)
            {
                int m = (a + b) / 2;
                if (T[m] <= tm) a = m; else b = m;
            }
            return T[b] <= tm ? b : a;
        }

        /// <summary>Position, velocity and group of every track present at time tm (linear
        /// between samples). group: -1 = every group.</summary>
        public List<State> At(double tm, int group = -1)
        {
            var res = new List<State>();
            for (int tr = 0; tr < Start.Length; tr++)
            {
                int k = SampleAt(tr, tm);
                if (k < 0) continue;
                if (group >= 0 && Group[k] != group) continue;
                int e = Start[tr] + Len[tr] - 1;
                double f = 0;
                int k2 = k;
                if (k < e && T[k + 1] > T[k])
                {
                    k2 = k + 1;
                    f = (tm - T[k]) / (T[k2] - T[k]);
                }
                res.Add(new State
                {
                    Track = tr, Id = Id[tr], Group = Group[k],
                    X = Lerp(X[k], X[k2], f), Y = Lerp(Y[k], Y[k2], f), Z = Lerp(Z[k], Z[k2], f),
                    Vx = Lerp(Vx[k], Vx[k2], f), Vy = Lerp(Vy[k], Vy[k2], f),
                    Speed = Lerp(Speed[k], Speed[k2], f), Top = TopOf(Group[k]),
                });
            }
            return res;
        }

        /// <summary>A track's path between t0 and t1 (samples, plus the exact ends).</summary>
        public List<(double X, double Y, double Z, double Speed)> Trail(int track, double t0, double t1)
        {
            var pts = new List<(double, double, double, double)>();
            int a = Start[track], b = a + Len[track] - 1;
            t0 = Math.Max(t0, T[a]); t1 = Math.Min(t1, T[b]);
            if (t1 <= t0) return pts;
            int k0 = SampleAt(track, t0), k1 = SampleAt(track, t1);
            if (k0 < 0 || k1 < 0) return pts;
            pts.Add(Interp(k0, b, t0));
            for (int k = k0 + 1; k <= k1; k++) pts.Add((X[k], Y[k], Z[k], Speed[k]));
            if (T[k1] < t1) pts.Add(Interp(k1, b, t1));
            return pts;
        }

        (double, double, double, double) Interp(int k, int last, double tm)
        {
            if (k >= last || T[k + 1] <= T[k]) return (X[k], Y[k], Z[k], Speed[k]);
            double f = (tm - T[k]) / (T[k + 1] - T[k]);
            return (Lerp(X[k], X[k + 1], f), Lerp(Y[k], Y[k + 1], f), Lerp(Z[k], Z[k + 1], f),
                    Lerp(Speed[k], Speed[k + 1], f));
        }

        static double Lerp(double a, double b, double f) => a + (b - a) * f;

        public struct State
        {
            public int Track, Id, Group;
            public double X, Y, Z, Vx, Vy, Speed, Top;
        }
    }
}
