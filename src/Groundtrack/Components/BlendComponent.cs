using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Groundtrack.Core;

namespace Groundtrack.Components
{
    /// <summary>Layer / interpolate fields. Two fields: T slides from the first (0) to the second
    /// (1). More fields: T slides along the list, or give Weights.</summary>
    public class BlendComponent : GH_Component
    {
        public BlendComponent()
            : base("Blend Fields", "Blend",
                   "Layer or interpolate movement fields (also of different sites, placed with Move / Rotate / Scale first). " +
                   "Where several have data the velocity is their weighted average; each fades in and out with its weight.",
                   Tab.Name, Tab.Flow) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e504");
        protected override Bitmap Icon => Icons.Get("blend");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Fields", "F", "Two or more fields", GH_ParamAccess.list);
            p.AddNumberParameter("T", "T", "0..1: from the first field to the last (empty: all equal)", GH_ParamAccess.item);
            p.AddNumberParameter("Weights", "W", "Optional: one weight per field (overrides T)", GH_ParamAccess.list);
            p[1].Optional = true;
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "The blended field", GH_ParamAccess.item);
            p.AddNumberParameter("Weights", "W", "The weights used", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var goos = new List<FieldGoo>();
            var weights = new List<double>();
            double t = double.NaN;
            if (!DA.GetDataList(0, goos)) return;
            var fields = goos.Where(g => g?.Value != null).Select(g => g.Value).ToList();
            if (fields.Count == 0) return;
            bool hasT = DA.GetData(1, ref t);
            bool hasW = DA.GetDataList(2, weights) && weights.Count > 0;
            var w = Weights(fields.Count, hasW ? weights : null, hasT ? t : double.NaN);
            var f = Blend.Mix(fields, w);
            DA.SetData(0, new FieldGoo(f));
            DA.SetDataList(1, w);
            Message = fields.Count == 1 ? "1 field" : string.Join(" / ", w.Select(x => x.ToString("0.##")));
        }

        /// <summary>Weights: given ones; else T along the list (0 = first, 1 = last, in between
        /// two neighbours share); else equal.</summary>
        public static List<double> Weights(int n, IList<double> given, double t)
        {
            var w = new double[n];
            if (given != null)
            {
                for (int k = 0; k < n; k++) w[k] = k < given.Count ? Math.Max(0, given[k]) : 0;
                return w.ToList();
            }
            if (double.IsNaN(t) || n == 1)
            {
                for (int k = 0; k < n; k++) w[k] = 1.0 / n;
                return w.ToList();
            }
            double pos = Math.Max(0, Math.Min(1, t)) * (n - 1);
            int a = (int)Math.Floor(pos);
            double f = pos - a;
            w[a] = 1 - f;
            if (a + 1 < n) w[a + 1] = f;
            return w.ToList();
        }
    }
}
