using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Groundtrack
{
    public class GroundtrackInfo : GH_AssemblyInfo
    {
        public override string Name => "groundtrack";
        public override Bitmap Icon => Icons.Get("field");
        public override string Description =>
            "Work with groundtrack movement data: fields, streamlines, particles, blending, crossings, replay and a timeline.";
        public override Guid Id => new Guid("6c1f4c2e-2d7a-4a59-9a43-6a0b2f1d3e21");
        public override string AuthorName => "groundtrack";
        public override string AuthorContact => "https://github.com/LCS3002/groundtrack-grasshopper";
        public override string Version => "0.5.2";
    }

    /// <summary>The tab and its panels.</summary>
    internal static class Tab
    {
        public const string Name = "groundtrack";
        public const string Data = "1 Data";
        public const string Flow = "2 Flow";
        public const string Shape = "3 Shape";
        public const string Forces = "4 Forces";
        public const string Analyse = "5 Analyse";
        public const string Time = "6 Time";
        public const string Colour = "7 Colour";
        public const string Export = "8 Export";
    }
}
