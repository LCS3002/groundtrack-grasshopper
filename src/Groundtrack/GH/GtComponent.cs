using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Groundtrack
{
    /// <summary>Base of the drawing components: speed-coloured preview (lines in colour buckets,
    /// coloured points, a coloured mesh) and baking with those colours. The plain geometry
    /// outputs stay hidden so the coloured preview is what you see.</summary>
    public abstract class GtComponent : GH_Component
    {
        protected GtComponent(string name, string nick, string desc, string sub)
            : base(name, nick, desc, Tab.Name, sub) { }

        /// <summary>Coloured points, drawn as round dots (grouped by colour).</summary>
        protected readonly Dots Cloud = new Dots();

        public sealed class Dots
        {
            readonly Dictionary<int, (List<Point3d> Pts, Color Colour)> _d = new Dictionary<int, (List<Point3d>, Color)>();
            public int Count { get; private set; }
            public void Add(Point3d p, Color c)
            {
                int key = c.ToArgb();
                if (!_d.TryGetValue(key, out var e)) _d[key] = e = (new List<Point3d>(), c);
                e.Pts.Add(p);
                Count++;
            }
            public void Clear() { _d.Clear(); Count = 0; }
            public BoundingBox Box()
            {
                var b = BoundingBox.Empty;
                foreach (var e in _d.Values) foreach (var p in e.Pts) b.Union(p);
                return b;
            }
            public void Draw(IGH_PreviewArgs args, float radius)
            {
                foreach (var e in _d.Values)
                    args.Display.DrawPoints(e.Pts, Rhino.Display.PointStyle.RoundSimple, radius, e.Colour);
            }
        }
        protected readonly List<Mesh> Meshes = new List<Mesh>();
        protected readonly List<Curve> BakeCurves = new List<Curve>();
        protected readonly List<Color> BakeColours = new List<Color>();
        protected readonly List<Point3d> BakePts = new List<Point3d>();
        protected readonly List<Color> BakePtColours = new List<Color>();
        List<(Line[] Lines, Color Colour)> _drawn;
        BoundingBox _box = BoundingBox.Empty;

        /// <summary>Line thickness of the preview (pixels).</summary>
        protected virtual int Thickness => 1;
        protected virtual float PointSize => 2.5f;

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            _drawn = null;
            Cloud.Clear();
            Meshes.Clear();
            BakeCurves.Clear(); BakeColours.Clear(); BakePts.Clear(); BakePtColours.Clear();
            _box = BoundingBox.Empty;
            foreach (var p in Params.Output) if (p is IGH_PreviewObject po) po.Hidden = true;
            ResetLines();
        }

        Preview.LineBuckets _buckets;
        protected Preview.LineBuckets Buckets => _buckets;
        void ResetLines() { _buckets = new Preview.LineBuckets(); }

        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();
            _drawn = _buckets.ToList();
            var b = BoundingBox.Empty;
            foreach (var (l, _) in _drawn) foreach (var line in l) { b.Union(line.From); b.Union(line.To); }
            if (Cloud.Count > 0) b.Union(Cloud.Box());
            foreach (var m in Meshes) b.Union(m.GetBoundingBox(false));
            _box = b;
        }

        public override BoundingBox ClippingBox => _box;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (Hidden || Locked) return;
            int th = Attributes.Selected ? Thickness + 1 : Thickness;
            if (_drawn != null) foreach (var (l, c) in _drawn) args.Display.DrawLines(l, c, th);
            if (Cloud.Count > 0) Cloud.Draw(args, Attributes.Selected ? PointSize + 1 : PointSize);
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            if (Hidden || Locked) return;
            foreach (var m in Meshes) args.Display.DrawMeshFalseColors(m);
        }

        public override bool IsPreviewCapable => true;
        public override bool IsBakeCapable => BakeCurves.Count > 0 || BakePts.Count > 0 || Meshes.Count > 0;

        public override void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids) => BakeGeometry(doc, null, obj_ids);

        public override void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
        {
            if (doc == null) return;
            Preview.BakeCurves(doc, att, obj_ids, BakeCurves, BakeColours, NickName);
            Preview.BakePoints(doc, att, obj_ids, BakePts, BakePtColours, NickName);
            foreach (var m in Meshes) obj_ids.Add(doc.Objects.AddMesh(m, att?.Duplicate()));
        }
    }
}
