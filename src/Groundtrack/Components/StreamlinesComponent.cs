using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack.Components
{
    public class StreamlinesComponent : GtComponent
    {
        public StreamlinesComponent()
            : base("Streamlines", "Stream",
                   "Evenly spaced flow lines through a field, coloured by speed. Start where most movement was; " +
                   "one Density slider sets how close they are.", Tab.Flow) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e502");
        protected override Bitmap Icon => Icons.Get("streamlines");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "A field", GH_ParamAccess.item);
            p.AddNumberParameter("Density", "D", "0..1: how close the lines are (0.5: about 3 cells apart)", GH_ParamAccess.item, 0.5);
            p.AddNumberParameter("Min length", "L", "Shortest line kept (m); empty = 3x the spacing", GH_ParamAccess.item);
            p.AddNumberParameter("Min confidence", "C", "Ignore cells less supported than this (0..1)", GH_ParamAccess.item, 0.05);
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Curves", "C", "The flow lines", GH_ParamAccess.list);
            p.AddNumberParameter("Speed", "Sp", "Mean speed of each line (m/s)", GH_ParamAccess.list);
            p.AddColourParameter("Colour", "Col", "Speed colour of each line", GH_ParamAccess.list);
            p.AddNumberParameter("Spacing", "S", "Distance between the lines (m)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FieldGoo g = null;
            double density = 0.5, minLen = double.NaN, minConf = 0.05;
            if (!DA.GetData(0, ref g) || g?.Value == null) return;
            DA.GetData(1, ref density);
            DA.GetData(2, ref minLen);
            DA.GetData(3, ref minConf);
            var f = g.Value;
            density = Math.Max(0.01, Math.Min(1, density));
            double spacing = f.WorldCell * (6.0 - 5.0 * density);
            var lines = Streamlines.Trace(f, spacing, minLen, minConf);
            var curves = new List<Curve>();
            var speeds = new List<double>();
            var colours = new List<Color>();
            foreach (var l in lines)
            {
                var pts = Enumerable.Range(0, l.Count).Select(k => new Point3d(l.X[k], l.Y[k], l.Z[k])).ToList();
                var sp = l.Speed.Select(v => v / Math.Max(f.Scale, 1e-9)).ToList();     // m/s of the data
                curves.Add(new PolylineCurve(pts));
                double mean = sp.Average();
                speeds.Add(mean);
                var c = Preview.SpeedColour(mean, l.Smax);
                colours.Add(c);
                Buckets.AddPolyline(pts, sp, l.Smax);
            }
            BakeCurves.AddRange(curves);
            BakeColours.AddRange(colours);
            DA.SetDataList(0, curves);
            DA.SetDataList(1, speeds);
            DA.SetDataList(2, colours);
            DA.SetData(3, spacing);
            Message = $"{lines.Count} lines";
        }
    }
}
