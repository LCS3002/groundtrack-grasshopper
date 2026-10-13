using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack.Components
{
    /// <summary>Where two sets of flows cross (in plan), the crossing angle, and a heat map.</summary>
    public class CrossingsComponent : GtComponent
    {
        public CrossingsComponent()
            : base("Crossings", "Cross",
                   "Where lines of set A cross lines of set B (e.g. people vs vehicles, or two sites laid over each other), " +
                   "at what angle, and a heat map of where flows intersect most. B empty: the lines of A with each other.",
                   Tab.Analyse) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e505");
        protected override Bitmap Icon => Icons.Get("crossings");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override float PointSize => 3;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("A", "A", "Curves (streamlines, trails, tracks ...)", GH_ParamAccess.list);
            p.AddCurveParameter("B", "B", "Curves; empty = A with itself", GH_ParamAccess.list);
            p.AddNumberParameter("Cell", "C", "Heat map cell (m); empty = automatic", GH_ParamAccess.item);
            p[1].Optional = true;
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter("Points", "P", "Crossing points", GH_ParamAccess.list);
            p.AddNumberParameter("Angle", "Ang", "Crossing angle (0..90 degrees; 90 = head on across)", GH_ParamAccess.list);
            p.AddIntegerParameter("A index", "iA", "Which curve of A", GH_ParamAccess.list);
            p.AddIntegerParameter("B index", "iB", "Which curve of B", GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Number of crossings", GH_ParamAccess.item);
            p.AddMeshParameter("Heat", "H", "Heat map of crossings (coloured mesh)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var ca = new List<Curve>();
            var cb = new List<Curve>();
            double cell = double.NaN;
            if (!DA.GetDataList(0, ca)) return;
            bool self = !DA.GetDataList(1, cb) || cb.Count == 0;
            DA.GetData(2, ref cell);
            var a = ca.Select(Poly).ToList();
            var b = self ? null : cb.Select(Poly).ToList();
            var hits = Crossings.Find(a, b);
            var heat = Crossings.Map(hits, cell);
            var pts = hits.Select(h => new Point3d(h.X, h.Y, h.Z)).ToList();
            foreach (var p in pts) Cloud.Add(p, Color.White);
            BakePts.AddRange(pts);
            BakePtColours.AddRange(pts.Select(_ => Color.White));
            var mesh = HeatMesh(heat, pts.Count > 0 ? pts.Average(p => p.Z) : 0);
            if (mesh != null) Meshes.Add(mesh);
            DA.SetDataList(0, pts);
            DA.SetDataList(1, hits.Select(h => h.Angle));
            DA.SetDataList(2, hits.Select(h => h.A));
            DA.SetDataList(3, hits.Select(h => h.B));
            DA.SetData(4, hits.Count);
            DA.SetData(5, mesh);
            Message = $"{hits.Count} crossings";
        }

        /// <summary>A curve as a list of points (polylines as they are, others divided ~0.25 m).</summary>
        static IList<(double X, double Y, double Z)> Poly(Curve c)
        {
            var res = new List<(double, double, double)>();
            if (c == null) return res;
            if (c.TryGetPolyline(out Polyline pl))
                foreach (var p in pl) res.Add((p.X, p.Y, p.Z));
            else
            {
                double len = c.GetLength();
                int n = Math.Max(2, Math.Min(5000, (int)Math.Ceiling(len / 0.25)));
                var ts = c.DivideByCount(n, true);
                if (ts != null) foreach (var t in ts) { var p = c.PointAt(t); res.Add((p.X, p.Y, p.Z)); }
            }
            return res;
        }

        /// <summary>The heat grid as a smooth mesh (shared vertices, colours blend), only where
        /// there is heat; purple (few crossings) to yellow (most).</summary>
        static Mesh HeatMesh(Crossings.Heat h, double z)
        {
            if (h.Value == null || h.Value.Length == 0) return null;
            int vx = h.Nx + 1, vy = h.Ny + 1;
            var val = new double[vx * vy];
            for (int j = 0; j < vy; j++)
                for (int i = 0; i < vx; i++)
                {
                    double s = 0;
                    int n = 0;
                    for (int dj = -1; dj <= 0; dj++)
                        for (int di = -1; di <= 0; di++)
                        {
                            int ci = i + di, cj = j + dj;
                            if (ci < 0 || cj < 0 || ci >= h.Nx || cj >= h.Ny) continue;
                            s += h.Value[cj * h.Nx + ci];
                            n++;
                        }
                    val[j * vx + i] = n > 0 ? s / n : 0;
                }
            var m = new Mesh();
            var index = new int[vx * vy];
            for (int k = 0; k < index.Length; k++) index[k] = -1;
            int V(int i, int j)
            {
                int k = j * vx + i;
                if (index[k] < 0)
                {
                    index[k] = m.Vertices.Add(h.X0 + i * h.Cell, h.Y0 + j * h.Cell, z - 0.05);
                    m.VertexColors.Add(Preview.HeatColour(0.3 + 0.7 * val[k]));
                }
                return index[k];
            }
            for (int j = 0; j < h.Ny; j++)
                for (int i = 0; i < h.Nx; i++)
                {
                    if (h.Value[j * h.Nx + i] < 0.08) continue;
                    m.Faces.AddFace(V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1));
                }
            if (m.Faces.Count == 0) return null;
            m.Normals.ComputeNormals();
            return m;
        }
    }
}
