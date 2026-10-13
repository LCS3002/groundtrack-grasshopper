using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Rhino;
using Rhino.Display;

namespace Groundtrack.UI
{
    /// <summary>Saves the active Rhino viewport (with the Grasshopper preview) as a PNG.</summary>
    public static class Recorder
    {
        public static string DefaultFolder()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "groundtrack frames", DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));

        /// <summary>The view to record: the active one, else the first.</summary>
        public static RhinoView View()
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return null;
            var all = doc.Views.GetViewList(true, false);
            return doc.Views.ActiveView ?? (all.Length > 0 ? all[0] : null);
        }

        /// <summary>Capture a frame; false when there is no view to capture.</summary>
        public static bool Capture(string path, Size size = default)
        {
            var view = View();
            if (view == null) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (size.IsEmpty) size = view.ActiveViewport.Size;
            view.Redraw();
            using (var bmp = view.CaptureToBitmap(size))
            {
                if (bmp == null) return false;
                bmp.Save(path, ImageFormat.Png);
            }
            return true;
        }
    }
}
