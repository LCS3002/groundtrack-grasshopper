using System;
using Eto.Drawing;
using Eto.Forms;
using Groundtrack.Components;

namespace Groundtrack.UI
{
    /// <summary>The floating timeline: play / pause, scrub, speed, loop, in / out, record.</summary>
    public sealed class TimelineWindow : Form
    {
        readonly TimelineComponent _c;
        readonly Slider _slider = new Slider { MinValue = 0, MaxValue = 1000, Width = 340 };
        readonly Label _time = new Label { Width = 120 };
        readonly Button _play = new Button { Text = "Play", Width = 70 };
        readonly Button _rec = new Button { Text = "Record", Width = 70 };
        readonly Button _start = new Button { Text = "|<", Width = 36 };
        readonly DropDown _speed = new DropDown();
        readonly DropDown _scale = new DropDown();
        readonly CheckBox _loop = new CheckBox { Text = "Loop" };
        readonly NumericStepper _in = new NumericStepper { DecimalPlaces = 1, Increment = 1, Width = 70 };
        readonly NumericStepper _out = new NumericStepper { DecimalPlaces = 1, Increment = 1, Width = 70 };
        readonly Label _status = new Label();
        static readonly double[] Speeds = { 0.25, 0.5, 1, 2, 4 };
        bool _sync;

        public TimelineWindow(TimelineComponent c)
        {
            _c = c;
            Title = "groundtrack timeline";
            Topmost = true;
            Resizable = true;
            Minimizable = false;
            Padding = new Padding(8);
            foreach (var s in Speeds) _speed.Items.Add($"{s:0.##}x");
            foreach (var k in new[] { 1, 2, 3, 4 }) _scale.Items.Add($"{k}x");
            _scale.ToolTip = "Record at this many times the viewport's size; lines and points scale with it";
            _scale.SelectedIndexChanged += (o, e) => { if (!_sync && _scale.SelectedIndex >= 0) _c.RecordScale = _scale.SelectedIndex + 1; };

            _play.Click += (o, e) => { if (_c.Playing) _c.Pause(); else _c.Play(); };
            _start.Click += (o, e) => _c.SetTime(_c.In);
            _rec.Click += (o, e) => { if (_c.Recording) _c.StopRecording(); else _c.StartRecording(); };
            _slider.ValueChanged += (o, e) =>
            {
                if (_sync) return;
                _c.SetTime(_c.In + (_c.Out - _c.In) * _slider.Value / 1000.0);
            };
            _speed.SelectedIndexChanged += (o, e) => { if (!_sync && _speed.SelectedIndex >= 0) _c.Speed = Speeds[_speed.SelectedIndex]; };
            _loop.CheckedChanged += (o, e) => { if (!_sync) _c.Loop = _loop.Checked == true; };
            _in.ValueChanged += (o, e) => { if (!_sync) { _c.In = _in.Value; _c.SetTime(_c.Current); } };
            _out.ValueChanged += (o, e) => { if (!_sync) { _c.Out = Math.Max(_in.Value + 0.1, _out.Value); _c.SetTime(_c.Current); } };

            var row1 = new StackLayout
            {
                Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center,
                Items = { _start, _play, _slider, _time },
            };
            var row2 = new StackLayout
            {
                Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center,
                Items = { new Label { Text = "Speed" }, _speed, _loop, new Label { Text = "  In" }, _in,
                          new Label { Text = "Out" }, _out, new Label { Text = "  Size" }, _scale, _rec },
            };
            Content = new StackLayout { Spacing = 6, Items = { row1, row2, _status } };
            _c.Changed += Sync;
            Closed += (o, e) => { IsClosed = true; _c.Changed -= Sync; };
            try { Owner = Rhino.UI.RhinoEtoApp.MainWindow; } catch { }
            Sync();
        }

        /// <summary>Closed by the user (it cannot be shown again; the component makes a new one).</summary>
        public bool IsClosed { get; private set; }

        void Sync()
        {
            Application.Instance.AsyncInvoke(() =>
            {
                if (IsDisposed || IsClosed) return;
                _sync = true;
                double span = Math.Max(_c.Out - _c.In, 1e-9);
                _slider.Value = (int)Math.Round((_c.Current - _c.In) / span * 1000);
                _time.Text = $"{_c.Current:0.00} s / {_c.Out:0.#} s";
                _play.Text = _c.Playing ? "Pause" : "Play";
                _rec.Text = _c.Recording ? "Stop" : "Record";
                _speed.SelectedIndex = Math.Max(0, Array.IndexOf(Speeds, _c.Speed));
                _loop.Checked = _c.Loop;
                _scale.SelectedIndex = Math.Max(0, Math.Min(3, _c.RecordScale - 1));
                _in.Value = _c.In;
                _out.Value = _c.Out;
                _status.Text = _c.Recording ? $"recording frame {_c.RecordedFrames} -> {_c.RecordFolder}"
                             : _c.RecordedFrames > 0 ? $"{_c.RecordedFrames} frames saved in {_c.RecordFolder}" : "";
                _sync = false;
            });
        }
    }
}
