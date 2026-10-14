using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Groundtrack.Core;

namespace Groundtrack.Components
{
    /// <summary>Fields (also bent, lofted, fused, bridged) to Houdini: a POP kit whose combined
    /// velocity volume is a force field for particles, plus the cells as a plain .geo.</summary>
    public class HoudiniExportComponent : GH_Component
    {
        public HoudiniExportComponent()
            : base("Houdini Export", "Houdini",
                   "Send fields to Houdini as a force field for POPs. Writes a groundtrack Houdini kit: every field a module, all of " +
                   "them combined into one velocity volume (vel) with a POP simulation running through it (Mode: force = the " +
                   "field pushes the particles, POP Advect by Volumes 'Update Force'; velocity = they follow it exactly), the " +
                   "volume also as a .vdb; and all cells as a .geo (P, v, Cd, density, speed) for your own setups. Paste the " +
                   "Houdini line into Houdini's Python Shell to build the scene.",
                   Tab.Name, Tab.Export) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e530");
        public override bool Read(GH_IO.Serialization.GH_IReader reader) => Compat.Read(this, reader, r => base.Read(r));
        protected override Bitmap Icon => Icons.Get("export");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new FieldParam(), "Fields", "F", "The fields (e.g. a Loft / Fuse / Bridge All)", GH_ParamAccess.list);
            p.AddTextParameter("Folder", "Dir", "Where to write; empty: Documents\\groundtrack houdini\\<Name>", GH_ParamAccess.item);
            p.AddTextParameter("Name", "N", "Name of the kit and its scene", GH_ParamAccess.item, "field");
            p.AddTextParameter("Mode", "M", "force (a force field: particles pushed towards the flow) / velocity (they follow it exactly)", GH_ParamAccess.item, "force");
            p.AddNumberParameter("Air resistance", "Air", "Force mode: how hard the field pulls the particles towards its velocity", GH_ParamAccess.item, 1.0);
            p.AddBooleanParameter("Export", "E", "Write the files (a button / toggle)", GH_ParamAccess.item, false);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Folder", "Dir", "The kit folder", GH_ParamAccess.item);
            p.AddTextParameter("Houdini", "H", "Paste into Houdini's Python Shell (Windows > Python Shell): builds the scene with the POP simulation", GH_ParamAccess.item);
            p.AddTextParameter("Files", "Fi", "The files written", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var goos = new List<FieldGoo>();
            string folder = null, name = "field", mode = "force";
            double air = 1;
            bool export = false;
            if (!DA.GetDataList(0, goos)) return;
            DA.GetData(1, ref folder); DA.GetData(2, ref name); DA.GetData(3, ref mode); DA.GetData(4, ref air); DA.GetData(5, ref export);
            var fields = goos.Where(q => q?.Value != null).Select(q => q.Value).ToList();
            name = Safe(string.IsNullOrWhiteSpace(name) ? "field" : name);
            if (string.IsNullOrWhiteSpace(folder))
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "groundtrack houdini", name);
            string line = $"import os; os.environ[\"GT_KIT\"] = r\"{folder}\"; exec(open(r\"{Path.Combine(folder, "hou_pop_build.py")}\").read())";
            DA.SetData(0, folder);
            DA.SetData(1, line);
            if (!export) { Message = "Export: off"; return; }
            try
            {
                var files = HoudiniKit.Write(folder, name, fields, mode.Trim().StartsWith("v", StringComparison.OrdinalIgnoreCase) ? "vel" : "force", air);
                DA.SetDataList(2, files);
                Message = $"{fields.Count} fields written";
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
            }
        }

        static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
    }

    /// <summary>Writes a groundtrack Houdini kit (the format of groundtrack's houdini-pop, with
    /// heights and vertical velocities) and a .geo of all cells.</summary>
    internal static class HoudiniKit
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Houdini's axes: X = east, Y = up, Z = south.</summary>
        static (double X, double Y, double Z) H(double x, double y, double z) => (x, z, -y);

        public static List<string> Write(string folder, string name, List<FieldData> fields, string advect, double airResist)
        {
            Directory.CreateDirectory(Path.Combine(folder, "fields"));
            var written = new List<string>();
            var modules = new List<string>();
            var names = new List<string>();
            var geo = new List<(double[] P, double[] V, double[] C, double Dens, double Speed, double Scale)>();
            for (int i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                string mod = $"{i:00}_{Slug(f.Site)}_{Slug(f.Clip)}";
                names.Add(mod);
                string csv = Path.Combine(folder, "fields", mod + ".csv");
                var sb = new StringBuilder("x,y,z,vx,vy,vz,speed,density,confidence,smax,cell\n");
                double cell = f.WorldCell, cx = 0, cz = 0, n = 0;
                var speeds = new List<double>();
                for (int k = 0; k < f.Count; k++)
                {
                    if (!f.Has[k]) continue;
                    var (wx, wy, wz) = f.CellWorld(k);
                    var (vx, vy, vz) = f.CellVelocity(k);
                    var p = H(wx, wy, wz);
                    var v = H(vx, vy, vz);
                    double sp = Math.Sqrt(vx * vx + vy * vy + vz * vz), top = Math.Max(f.Smax[k] * f.Scale, 1e-6);
                    sb.Append(string.Format(Inv, "{0:0.###},{1:0.###},{2:0.###},{3:0.####},{4:0.####},{5:0.####},{6:0.####},{7:0.######},{8:0.####},{9:0.###},{10:0.####}\n",
                                            p.X, p.Y, p.Z, v.X, v.Y, v.Z, sp, f.Density[k], f.Confidence[k], top, cell));
                    cx += p.X; cz += p.Z; n++;
                    speeds.Add(sp);
                    var (r, g, b) = Ramp.At(sp / top);
                    geo.Add((new[] { p.X, p.Y, p.Z }, new[] { v.X, v.Y, v.Z }, new[] { r / 255.0, g / 255.0, b / 255.0 }, f.Density[k], sp, 0.75 * cell));
                }
                File.WriteAllText(csv, sb.ToString());
                written.Add(csv);
                speeds.Sort();
                double med = speeds.Count > 0 ? Math.Max(speeds[speeds.Count / 2], 0.2) : 1;
                var (x0, y0, z0, x1, y1, z1) = f.WorldBox();
                double span = Math.Max(Math.Max(x1 - x0, y1 - y0), z1 - z0);
                double life = Math.Max(4, Math.Min(40, 1.2 * span / med));
                modules.Add(string.Format(Inv,
                    "  {{\"name\": \"{0}\", \"fields\": {{\"field\": \"fields/{0}.csv\"}}, \"group\": \"field\", \"centre\": [{1:0.###}, {2:0.###}], " +
                    "\"cell\": {3:0.####}, \"rate\": 300, \"life\": {4:0.#}, \"noise\": 0.3, \"min_speed\": 0.02, \"trail_frames\": 24, " +
                    "\"seeds\": 20000, \"min_confidence\": 0.0, \"visible\": false, \"advect\": \"{5}\", \"airresist\": {6:0.###}}}",
                    mod, n > 0 ? cx / n : 0, n > 0 ? cz / n : 0, cell, life, advect, airResist));
            }
            var ramp = string.Join(", ", Ramp.SrgbStops.Select(s => string.Format(Inv, "[{0:0.##}, [{1:0.####}, {2:0.####}, {3:0.####}]]", s[0], s[1], s[2], s[3])));
            string note = $"groundtrack for Grasshopper: {fields.Count} fields. 'combined' runs one POP simulation through all of them " +
                          $"(their velocity volume OUT_FIELD, field 'vel'); POP Advect by Volumes in {(advect == "force" ? "Update Force mode: a force field" : "Update Velocity mode")}. " +
                          "Use OUT_FIELD (or the .vdb) as the velocity source in your own POP networks.";
            string json = "{\n \"site\": \"" + name + "\",\n \"origin\": [0, 0],\n \"fps\": 24,\n \"frames\": 480,\n" +
                          " \"ramp\": [" + ramp + "],\n \"modules\": [\n" + string.Join(",\n", modules) + "\n ],\n" +
                          " \"combined\": [" + string.Join(", ", names.Select(m => "\"" + m + "\"")) + "],\n" +
                          " \"show_combined\": true,\n \"vdb\": true,\n \"note\": \"" + note.Replace("\"", "'") + "\"\n}\n";
            string mj = Path.Combine(folder, "modules.json");
            File.WriteAllText(mj, json);
            written.Add(mj);
            string builder = Path.Combine(folder, "hou_pop_build.py");
            using (var s = typeof(HoudiniKit).Assembly.GetManifestResourceStream("hou_pop_build.py"))
            using (var r = new StreamReader(s))
                File.WriteAllText(builder, r.ReadToEnd());
            written.Add(builder);
            string geoPath = Path.Combine(folder, name + "_field.geo");
            WriteGeo(geoPath, geo);
            written.Add(geoPath);
            string readme = Path.Combine(folder, "README.txt");
            File.WriteAllText(readme, Readme(name, folder));
            written.Add(readme);
            return written;
        }

        static string Slug(string s) => string.IsNullOrEmpty(s) ? "field" : new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        /// <summary>Points with P, v, Cd, density, speed, pscale: Houdini's JSON geometry (.geo).</summary>
        static void WriteGeo(string path, List<(double[] P, double[] V, double[] C, double Dens, double Speed, double Scale)> pts)
        {
            var sb = new StringBuilder();
            string Tuples(Func<int, double[]> get)
            {
                var t = new StringBuilder();
                for (int i = 0; i < pts.Count; i++)
                {
                    var v = get(i);
                    if (i > 0) t.Append(',');
                    t.Append('[').Append(string.Join(",", v.Select(x => x.ToString("0.#####", Inv)))).Append(']');
                }
                return t.ToString();
            }
            string Attr(string attrName, int size, string kind, string tuples)
                => "[[\"scope\",\"public\",\"type\",\"numeric\",\"name\",\"" + attrName + "\",\"options\",{" +
                   (kind == null ? "" : "\"type\":{\"type\":\"string\",\"value\":\"" + kind + "\"}") + "}]," +
                   "[\"size\"," + size + ",\"storage\",\"fpreal32\",\"defaults\",[\"size\",1,\"storage\",\"fpreal64\",\"values\",[0]]," +
                   "\"values\",[\"size\"," + size + ",\"storage\",\"fpreal32\",\"tuples\",[" + tuples + "]]]]";
            sb.Append("[\"fileversion\",\"21.0.729\",\"hasindex\",false,\"pointcount\",").Append(pts.Count)
              .Append(",\"vertexcount\",0,\"primitivecount\",0,\"info\",{\"software\":\"groundtrack for Grasshopper\"},")
              .Append("\"topology\",[\"pointref\",[\"indices\",[]]],\"attributes\",[\"pointattributes\",[");
            sb.Append(Attr("P", 3, "point", Tuples(i => pts[i].P))).Append(',');
            sb.Append(Attr("v", 3, "vector", Tuples(i => pts[i].V))).Append(',');
            sb.Append(Attr("Cd", 3, "color", Tuples(i => pts[i].C))).Append(',');
            sb.Append(Attr("density", 1, null, Tuples(i => new[] { pts[i].Dens }))).Append(',');
            sb.Append(Attr("speed", 1, null, Tuples(i => new[] { pts[i].Speed }))).Append(',');
            sb.Append(Attr("pscale", 1, null, Tuples(i => new[] { pts[i].Scale })));
            sb.Append("]],\"primitives\",[]]");
            File.WriteAllText(path, sb.ToString());
        }

        static string Readme(string name, string folder) =>
$@"groundtrack for Grasshopper -> Houdini: {name}

Build the scene (Houdini, also Apprentice): Windows > Python Shell, paste

    import os; os.environ[""GT_KIT""] = r""{folder}""; exec(open(r""{Path.Combine(folder, "hou_pop_build.py")}"").read())

It writes {name}_pop.hipnc (.hip / .hiplc with other licences) here and the velocity volumes as .vdb.
In the scene, 'combined' runs one POP simulation through all the fields: OUT_FIELD is their velocity
volume (field 'vel'), the POP network pushes the particles with it (POP Advect by Volumes).

As a force field in your own POP network:
  POP Advect by Volumes  ->  Velocity Source: SOP, SOP Path: /obj/combined/OUT_FIELD (or a File SOP
  loading {name}_combined_vel.vdb), Velocity Field: vel, Advection Type: Update Force (+ Air
  Resistance) for a force field, Update Velocity to follow it exactly.

{name}_field.geo: every cell as a point (P, v, Cd, density, speed, pscale) - a File SOP reads it.
Axes: Houdini's - X east, Y up, Z south (Rhino's Z is Houdini's Y). Metres.
";
    }
}
