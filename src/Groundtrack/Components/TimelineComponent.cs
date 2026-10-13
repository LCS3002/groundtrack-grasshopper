using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Groundtrack.UI;

namespace Groundtrack.Components
{
    /// <summary>One clock for everything that moves: Particles, Replay and time-sliced Fields.
    /// Double-click (or right-click > Open timeline) for the timeline window: play, pause,
    /// scrub, speed, loop, in / out range and Record (viewport -> PNG sequence).</summary>
    public class TimelineComponent : GH_Component
    {
        public TimelineComponent()
            : base("Timeline", "Time",
                   "The clock (seconds) for Particles, Replay and time-sliced Fields. Double-click it for the timeline window " +
                   "(play / pause / scrub / speed / loop / in-out range / record the viewport as PNGs).",
                   Tab.Name, Tab.Time) { }

        public override Guid ComponentGuid => new Guid("3f6a1d0e-8b2c-4d5e-9f01-a1b2c3d4e506");
        protected override Bitmap Icon => Icons.Get("timeline");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        // ---- state (saved with the file)
        public double Current, In = 0, Out = 30, Speed = 1, Fps = 30;
        public bool Loop = true;
        public bool Playing { get; private set; }
        public bool Recording { get; private set; }
        public string RecordFolder = "";
        public int RecordedFrames { get; private set; }
        public event Action Changed;

        Eto.Forms.UITimer _timer;
        DateTime _last;
        TimelineWindow _window;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Start", "S", "Start of the range (s); empty = keep the window's", GH_ParamAccess.item);
            p.AddNumberParameter("End", "E", "End of the range (s); empty = keep the window's (30 s at first)", GH_ParamAccess.item);
            p.AddNumberParameter("FPS", "FPS", "Frames per second (playback and recording)", GH_ParamAccess.item, 30.0);
            p.AddBooleanParameter("Window", "Win", "Open the timeline window", GH_ParamAccess.item, false);
            p[0].Optional = true;
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("T", "T", "Time (s)", GH_ParamAccess.item);
            p.AddIntegerParameter("Frame", "F", "Frame number (from Start, at FPS)", GH_ParamAccess.item);
            p.AddBooleanParameter("Playing", "P", "True while playing or recording", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double s = double.NaN, e = double.NaN, fps = 30;
            bool win = false;
            if (DA.GetData(0, ref s)) In = s;
            if (DA.GetData(1, ref e)) Out = e;
            if (DA.GetData(2, ref fps)) Fps = Math.Max(1, Math.Min(240, fps));
            DA.GetData(3, ref win);
            if (Out <= In) Out = In + 1;
            Current = Math.Max(In, Math.Min(Out, Current));
            if (win) Rhino.RhinoApp.InvokeOnUiThread((Action)ShowWindow);
            DA.SetData(0, Current);
            DA.SetData(1, (int)Math.Round((Current - In) * Fps));
            DA.SetData(2, Playing || Recording);
            Message = Recording ? $"REC {Current:0.00} s" : $"{Current:0.00} s";
        }

        // ---- control (window, menu, scripts) ------------------------------------------------
        public void SetTime(double t, bool solve = true)
        {
            Current = Math.Max(In, Math.Min(Out, t));
            if (solve) ExpireSolution(true);
            Changed?.Invoke();
        }

        public void Play()
        {
            if (Recording) return;
            if (Current >= Out - 1e-9) Current = In;
            Playing = true;
            _last = DateTime.Now;
            if (_timer == null)
            {
                _timer = new Eto.Forms.UITimer();
                _timer.Elapsed += (o, a) => Tick();
            }
            _timer.Interval = 1.0 / Fps;
            _timer.Start();
            Changed?.Invoke();
        }

        public void Pause()
        {
            Playing = false;
            _timer?.Stop();
            ExpireSolution(true);
            Changed?.Invoke();
        }

        void Tick()
        {
            if (!Playing) return;
            var now = DateTime.Now;
            double dt = Math.Min((now - _last).TotalSeconds, 0.25) * Speed;
            _last = now;
            double t = Current + dt;
            if (t > Out)
            {
                if (Loop) t = In + (t - Out) % Math.Max(Out - In, 1e-6);
                else { Current = Out; Pause(); return; }
            }
            SetTime(t);
        }

        /// <summary>Record In..Out frame by frame: each frame is saved after its solution has
        /// finished (and the viewport redrawn), then the next is computed.</summary>
        public void StartRecording(string folder = null)
        {
            if (Playing) Pause();
            var doc = OnPingDocument();
            if (doc == null) return;
            RecordFolder = string.IsNullOrEmpty(folder) ? Recorder.DefaultFolder() : folder;
            Directory.CreateDirectory(RecordFolder);
            RecordedFrames = 0;
            Recording = true;
            doc.SolutionEnd += OnSolutionEnd;
            SetTime(In);
        }

        public void StopRecording()
        {
            Recording = false;
            var doc = OnPingDocument();
            if (doc != null) doc.SolutionEnd -= OnSolutionEnd;
            ExpireSolution(true);
            Changed?.Invoke();
        }

        void OnSolutionEnd(object sender, GH_SolutionEventArgs e)
        {
            if (!Recording) return;
            var doc = e.Document;
            // capture after Grasshopper has handed the new preview to the viewport
            doc.ScheduleSolution(1, d =>
            {
                if (!Recording) return;
                string path = Path.Combine(RecordFolder, $"frame_{RecordedFrames:00000}.png");
                if (!Recorder.Capture(path))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Rhino view to record");
                    StopRecording();
                    return;
                }
                RecordedFrames++;
                double next = In + RecordedFrames / Fps;
                if (next > Out + 1e-9) { StopRecording(); return; }
                Current = next;
                ExpireSolution(false);
                Changed?.Invoke();
            });
        }

        /// <summary>Open the timeline window. A closed window cannot be shown again (Windows), so
        /// closing forgets it and the next call builds a new one - also mid-animation.</summary>
        public void ShowWindow()
        {
            if (_window != null && (_window.IsDisposed || _window.IsClosed)) _window = null;
            if (_window != null)
            {
                try
                {
                    _window.Show();
                    _window.BringToFront();
                    return;
                }
                catch (Exception)
                {
                    _window = null;          // could not be shown again: make a new one
                }
            }
            var w = new TimelineWindow(this);
            w.Closed += (s, e) => { if (ReferenceEquals(_window, w)) _window = null; };
            _window = w;
            w.Show();
            w.BringToFront();
        }

        /// <summary>The window if one is open (for tests).</summary>
        public TimelineWindow Window => _window;

        // ---- UI hooks ------------------------------------------------------------------------
        public override void CreateAttributes() => m_attributes = new TimelineAttributes(this);

        sealed class TimelineAttributes : GH_ComponentAttributes
        {
            public TimelineAttributes(TimelineComponent owner) : base(owner) { }

            public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                ((TimelineComponent)Owner).ShowWindow();
                return GH_ObjectResponse.Handled;
            }
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Open timeline window", (s, e) => ShowWindow());
            Menu_AppendItem(menu, Playing ? "Pause" : "Play", (s, e) => { if (Playing) Pause(); else Play(); });
            Menu_AppendItem(menu, "Record In..Out to PNGs", (s, e) => StartRecording());
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            _timer?.Stop();
            Recording = false;
            Playing = false;
            if (_window != null && !_window.IsDisposed) _window.Close();
            base.RemovedFromDocument(document);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetDouble("gt_current", Current);
            writer.SetDouble("gt_in", In);
            writer.SetDouble("gt_out", Out);
            writer.SetDouble("gt_speed", Speed);
            writer.SetDouble("gt_fps", Fps);
            writer.SetBoolean("gt_loop", Loop);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            double v = 0;
            bool b = true;
            if (reader.TryGetDouble("gt_current", ref v)) Current = v;
            if (reader.TryGetDouble("gt_in", ref v)) In = v;
            if (reader.TryGetDouble("gt_out", ref v)) Out = v;
            if (reader.TryGetDouble("gt_speed", ref v)) Speed = v;
            if (reader.TryGetDouble("gt_fps", ref v)) Fps = v;
            if (reader.TryGetBoolean("gt_loop", ref b)) Loop = b;
            return Compat.Read(this, reader, r => base.Read(r));
        }
    }
}
