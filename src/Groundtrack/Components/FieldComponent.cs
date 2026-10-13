using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Groundtrack.Core;
using Rhino.Geometry;

namespace Groundtrack.Components
{
    /// <summary>Loads a field from a groundtrack results folder. Site / Clip / Group come as
    /// dropdowns (created when the component is placed, refilled when the site changes).</summary>
    public class FieldComponent : GH_Component
    {
        public FieldComponent()
            : base("Field", "Field",
                   "A movement field from your groundtrack results: the whole site or one clip, one group (people, vehicles ...) or all. " +
                   "Metres around the site's origin, Easting -> +X, Northing -> +Y, Z = ground height. " +
                   "Give a Time to get the movement in that moment instead (from the tracks).",
                   Tab.Name, Tab.Data) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e501");
        protected override Bitmap Icon => Icons.Get("field");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        const int IFolder = 0, ISite = 1, IClip = 2, IGroup = 3, ITime = 4, IWindow = 5, IHeights = 6;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Folder", "Dir", "The GROUNDTRACK RESULTS folder (found automatically under Documents if empty; remembered)", GH_ParamAccess.item);
            p.AddTextParameter("Site", "S", "Site (dropdown)", GH_ParamAccess.item);
            p.AddTextParameter("Clip", "C", "'whole' (all clips of the site combined) or one clip (dropdown)", GH_ParamAccess.item);
            p.AddTextParameter("Group", "G", "all / people / cycles / vehicles / buses (dropdown)", GH_ParamAccess.item);
            p.AddNumberParameter("Time", "T", "Optional: the field of the movement around this time (s), from the tracks - connect a Timeline", GH_ParamAccess.item);
            p.AddNumberParameter("Window", "W", "Time window (s) around Time", GH_ParamAccess.item, 5.0);
            p.AddBooleanParameter("Heights", "H", "Follow the ground (terraces, steps) from the track heights", GH_ParamAccess.item, true);
            for (int k = 0; k <= ITime; k++) p[k].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Field", "F", "The field (place it with Move / Rotate / Scale)", GH_ParamAccess.item);
            p.AddPointParameter("Points", "P", "Cell centres (cells with data)", GH_ParamAccess.list);
            p.AddVectorParameter("Vectors", "V", "Velocity of each cell (m/s)", GH_ParamAccess.list);
            p.AddNumberParameter("Speed", "Sp", "Mean speed of each cell (m/s)", GH_ParamAccess.list);
            p.AddNumberParameter("Density", "D", "How much movement passed each cell", GH_ParamAccess.list);
            p.AddColourParameter("Colour", "Col", "Speed colour of each cell", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "What was loaded", GH_ParamAccess.item);
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            if (Params.Output[1] is IGH_PreviewObject po) po.Hidden = true;   // the field draws its arrows
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string folder = null, site = null, clip = null, group = null;
            double t = double.NaN, window = 5;
            bool heights = true;
            DA.GetData(IFolder, ref folder);
            DA.GetData(ISite, ref site);
            DA.GetData(IClip, ref clip);
            DA.GetData(IGroup, ref group);
            bool timed = DA.GetData(ITime, ref t);
            DA.GetData(IWindow, ref window);
            DA.GetData(IHeights, ref heights);

            string root = Root(folder);
            if (string.IsNullOrEmpty(root))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No results folder: connect the GROUNDTRACK RESULTS folder to Folder");
                return;
            }
            var sites = Catalog.Sites(root);
            if (sites.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"No groundtrack sites in {root}");
                return;
            }
            var s = sites.FirstOrDefault(x => Same(x.Name, site) || Same(Path.GetFileName(x.Folder), site)) ?? sites[0];
            if (!string.IsNullOrEmpty(site) && !Same(s.Name, site) && !Same(Path.GetFileName(s.Folder), site))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"No site '{site}': using {s.Name}");
            if (clip == null || !s.Clips.Contains(clip)) clip = Catalog.Whole;
            var groups = Catalog.FieldFiles(s, clip).Keys.ToList();
            if (group == null || !groups.Contains(group)) group = groups.FirstOrDefault();
            RefreshLists(sites.Select(x => x.Name).ToList(), s.Clips, groups, s.Name, clip, group);

            FieldData f;
            try
            {
                f = Catalog.Load(s, clip, group, heights);
                if (timed)
                {
                    var tracks = Catalog.Tracks(f, s);
                    if (tracks == null)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No tracks for this field: Time ignored");
                    else
                        f = Catalog.FromTracks(f, tracks, t - window / 2, t + window / 2, group);
                }
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                return;
            }

            var pts = new List<Point3d>();
            var vec = new List<Vector3d>();
            var spd = new List<double>();
            var den = new List<double>();
            var col = new List<Color>();
            for (int k = 0; k < f.Count; k++)
            {
                if (!f.Has[k]) continue;
                var (x, y, z) = f.CellWorld(k);
                var (vx, vy, vz) = f.CellVelocity(k);
                pts.Add(new Point3d(x, y, z));
                vec.Add(new Vector3d(vx, vy, vz));
                spd.Add(f.Speed[k]);
                den.Add(f.Density[k]);
                col.Add(Preview.SpeedColour(f.Speed[k], f.Smax[k]));
            }
            DA.SetData(0, new FieldGoo(f));
            DA.SetDataList(1, pts);
            DA.SetDataList(2, vec);
            DA.SetDataList(3, spd);
            DA.SetDataList(4, den);
            DA.SetDataList(5, col);
            DA.SetData(6, $"{f.Name}: {f.Valid} cells of {f.Cell:0.##} m; origin E {f.OriginE:0.0} N {f.OriginN:0.0}" +
                          (f.Z0 != 0 ? $", ground {f.Z0:0.00} m" : "") + $"; {root}");
            Message = $"{s.Name}\n{clip} / {group}";
        }

        static bool Same(string a, string b) => !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>The results folder: the input, else the remembered one, else found under Documents.</summary>
        static string Root(string folder)
        {
            const string key = "groundtrack.results";
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Grasshopper.Instances.Settings.SetValue(key, folder);
                return folder;
            }
            string saved = Grasshopper.Instances.Settings.GetValue(key, "");
            if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved)) return saved;
            string found = Catalog.FindRoot();
            if (!string.IsNullOrEmpty(found)) Grasshopper.Instances.Settings.SetValue(key, found);
            return found;
        }

        // ---- dropdowns --------------------------------------------------------------------
        const string Tag = "groundtrack:";
        bool _read;

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            _read = true;
            return Compat.Read(this, reader, r => base.Read(r));
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            // place dropdowns on the empty Site / Clip / Group inputs (not when read from a file / pasted)
            if (_read) return;
            var piv = Attributes.Pivot;
            int n = 0;
            foreach (var (ix, name) in new[] { (ISite, "Site"), (IClip, "Clip"), (IGroup, "Group") })
            {
                if (Params.Input[ix].SourceCount > 0) continue;
                var list = new GH_ValueList { NickName = name, Description = Tag + name.ToLowerInvariant() };
                list.ListMode = GH_ValueListMode.DropDown;
                list.ListItems.Clear();
                list.CreateAttributes();
                list.Attributes.Pivot = new PointF(piv.X - 230, piv.Y - 30 + 22 * n++);
                document.AddObject(list, false);
                Params.Input[ix].AddSource(list);
            }
            ExpireSolution(true);
        }

        void RefreshLists(List<string> sites, List<string> clips, List<string> groups, string site, string clip, string group)
        {
            var doc = OnPingDocument();
            if (doc == null) return;
            var todo = new List<(GH_ValueList, List<string>, string)>();
            foreach (var (ix, items, sel) in new[] { (ISite, sites, site), (IClip, clips, clip), (IGroup, groups, group) })
            {
                if (Params.Input[ix].SourceCount != 1) continue;
                if (!(Params.Input[ix].Sources[0] is GH_ValueList list) || !(list.Description ?? "").StartsWith(Tag)) continue;
                var now = list.ListItems.Select(i => i.Name).ToList();
                bool same = now.SequenceEqual(items);
                bool selOk = list.SelectedItems.Count == 1 && list.SelectedItems[0].Name == sel;
                if (!same || !selOk) todo.Add((list, items, sel));
            }
            if (todo.Count == 0) return;
            doc.ScheduleSolution(5, d =>
            {
                foreach (var (list, items, sel) in todo)
                {
                    string keep = list.SelectedItems.Count == 1 ? list.SelectedItems[0].Name : sel;
                    list.ListItems.Clear();
                    foreach (var it in items) list.ListItems.Add(new GH_ValueListItem(it, "\"" + it + "\""));
                    int k = items.IndexOf(items.Contains(keep) ? keep : sel);
                    if (k >= 0) list.SelectItem(k);
                    list.ExpireSolution(false);
                }
            });
        }
    }
}
