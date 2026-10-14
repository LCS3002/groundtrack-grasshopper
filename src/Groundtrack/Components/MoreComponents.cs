using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack.Components
{
    /// <summary>Several fields as one: sites, clips and groups together.</summary>
    public class MergeComponent : GH_Component
    {
        public MergeComponent()
            : base("Merge Fields", "Merge",
                   "Several fields as one field (e.g. Field nodes for different sites / clips / groups, placed with Move first). " +
                   "Where they overlap the busier flow counts more. For a slider between fields use Blend Fields.",
                   Tab.Name, Tab.Flow) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e508");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("merge");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Fields", "F", "Any number of fields", GH_ParamAccess.list);
            p.AddNumberParameter("Feather", "Fe", "Each field fades in over this many metres from its edge: soft overlaps, no seams or height jumps (0 = hard)", GH_ParamAccess.item, 4.0);
            p.AddNumberParameter("Smooth", "Sm", "Smooth the result over this many metres (0 = off)", GH_ParamAccess.item, 1.5);
            p.AddBooleanParameter("Fill", "Fill", "Close small gaps between the fields while smoothing", GH_ParamAccess.item, true);
            p.AddNumberParameter("Max slope", "MS", "Steepest the ground may get (rise per run; 1 = 45 deg): cliffs where fields at different heights meet become ramps (0 = off)", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
            => p.AddParameter(new FieldParam(), "Field", "F", "One field", GH_ParamAccess.item);

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var goos = new List<FieldGoo>();
            if (!DA.GetDataList(0, goos)) return;
            var fields = goos.Where(g => g?.Value != null).Select(g => g.Value).ToList();
            if (fields.Count == 0) return;
            if (fields.Any(f => f.Warped))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Bent fields (morphs, Warp Field, Loft, Bridge) are combined as they were before bending: bend after combining");
                fields = fields.Select(f => f.Warped ? f.Unwarped() : f).ToList();
            }
            double feather = 4, smooth = 1.5;
            bool fill = true;
            double maxSlope = 1;
            DA.GetData(1, ref feather); DA.GetData(2, ref smooth); DA.GetData(3, ref fill); DA.GetData(4, ref maxSlope);
            var m = Blend.Merge(fields, Math.Max(0, feather), Math.Max(0, smooth), fill, Math.Max(0, maxSlope));
            DA.SetData(0, new FieldGoo(m));
            Message = $"{fields.Count} fields";
        }
    }

    /// <summary>Draw your own flow: a field along curves.</summary>
    public class CurveFieldComponent : GH_Component
    {
        public CurveFieldComponent()
            : base("Field from Curves", "CrvField",
                   "A field drawn with curves: the flow runs along each curve (in its direction) at Speed, fading out over Width. " +
                   "Merge / Blend it with measured fields, or run Streamlines / Particles through it.",
                   Tab.Name, Tab.Data) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e509");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("curvefield");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Curves", "C", "Flow lines (their direction is the flow's)", GH_ParamAccess.list);
            p.AddNumberParameter("Speed", "Sp", "Speed along them (m/s)", GH_ParamAccess.item, 1.3);
            p.AddNumberParameter("Width", "W", "How far to each side the flow reaches (m)", GH_ParamAccess.item, 3.0);
            p.AddNumberParameter("Cell", "Cell", "Grid cell (m); empty = automatic", GH_ParamAccess.item);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
            => p.AddParameter(new FieldParam(), "Field", "F", "The drawn field", GH_ParamAccess.item);

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var cs = new List<Curve>();
            double speed = 1.3, width = 3, cell = double.NaN;
            if (!DA.GetDataList(0, cs)) return;
            DA.GetData(1, ref speed); DA.GetData(2, ref width); DA.GetData(3, ref cell);
            var f = CurveField.Make(cs.Select(c => Geo.Poly(c, 0.25)).ToList(), speed, Math.Max(width, 0.01), cell);
            if (f == null) return;
            DA.SetData(0, new FieldGoo(f));
            Message = $"{f.Valid} cells";
        }
    }

    /// <summary>A colour gradient for Streamlines / Particles / Replay / Crossings.</summary>
    public class GradientComponent : GH_Component
    {
        public GradientComponent()
            : base("Gradient", "Grad",
                   "Recolour: a preset (groundtrack, vivid, inferno, viridis, magma, greys, white-pink, blue-red, mono) or your own " +
                   "colours (evenly spaced, or at Positions 0..1); Saturation / Brightness make them punchier. Plug into the Gradient " +
                   "input of Streamlines, Particles, Replay, Crossings or Field Surface.",
                   Tab.Name, Tab.Colour) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e510");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("gradient");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Preset", "P", string.Join(" / ", Gradient.Presets), GH_ParamAccess.item, "groundtrack");
            p.AddColourParameter("Colours", "C", "Your own colours (override the preset)", GH_ParamAccess.list);
            p.AddNumberParameter("Positions", "T", "Where each colour sits (0..1); empty = evenly", GH_ParamAccess.list);
            p.AddBooleanParameter("Reverse", "R", "Flip it", GH_ParamAccess.item, false);
            p.AddNumberParameter("Saturation", "Sat", "x the colours' saturation (1 = as is; 1.3-1.8 = punchier, e.g. for exported images)", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Brightness", "Br", "x the colours' brightness (1 = as is)", GH_ParamAccess.item, 1.0);
            p[1].Optional = true;
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddParameter(new GradientParam(), "Gradient", "G", "The gradient", GH_ParamAccess.item);
            p.AddColourParameter("Swatch", "S", "11 sample colours (0, 0.1 ... 1)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string preset = "groundtrack";
            var cols = new List<Color>();
            var pos = new List<double>();
            bool rev = false;
            DA.GetData(0, ref preset);
            DA.GetDataList(1, cols);
            DA.GetDataList(2, pos);
            DA.GetData(3, ref rev);
            double sat = 1, bright = 1;
            if (Params.Input.Count > 5) { DA.GetData(4, ref sat); DA.GetData(5, ref bright); }
            var g = cols.Count > 0
                ? Gradient.FromColours(cols.Select(c => (c.R, c.G, c.B)).ToList(), pos.Count == cols.Count ? pos : null)
                : Gradient.Preset(preset);
            if (rev) g = g.Reversed();
            g = g.Adjusted(Math.Max(0, sat), Math.Max(0, bright));
            DA.SetData(0, new GradientGoo(g));
            DA.SetDataList(1, Enumerable.Range(0, 11).Select(k => Preview.Colour(k / 10.0, g)));
            Message = cols.Count > 0 ? "custom" : g.Name;
        }
    }
}
