using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Windows.Forms;

namespace AgentClient
{
    /// <summary>
    /// Small draggable pill showing time left. Always on top, hidden from Alt+Tab.
    /// Position is persisted to pill_state.json.
    /// </summary>
    public sealed class PillTimerForm : Form
    {
        private readonly Label _label;
        private Point _dragStart;
        private bool _dragging;
        private string _stateFile = "pill_state.json";

        private readonly int _w = 220;
        private readonly int _h = 48;

        public PillTimerForm()
        {
            // Window chrome
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;

            Size = new Size(_w, _h);
            SetRoundedRegion();

            // Label centered
            _label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Text = "--:--"
            };
            Controls.Add(_label);

            // Drag to move
            MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
            MouseMove += (_, e) =>
            {
                if (_dragging)
                {
                    var dx = e.X - _dragStart.X;
                    var dy = e.Y - _dragStart.Y;
                    Location = new Point(Location.X + dx, Location.Y + dy);
                }
            };
            MouseUp += (_, e) =>
            {
                if (_dragging && e.Button == MouseButtons.Left)
                {
                    _dragging = false;
                    SavePosition();
                }
            };

            // Right-click to snap to screen corner (simple helper)
            ContextMenuStrip = new ContextMenuStrip();
            ContextMenuStrip.Items.Add("Snap to bottom-right", null, (_, __) => SnapToBottomRight());
            ContextMenuStrip.Items.Add("Snap to top-right", null, (_, __) => SnapToTopRight());

            LoadPosition();
        }

        /// <summary>Hide from Alt+Tab.</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SetRoundedRegion();
        }

        private void SetRoundedRegion()
        {
            int radius = Height / 2;
            using var path = new GraphicsPath();
            path.StartFigure();
            path.AddArc(new Rectangle(0, 0, radius, radius), 180, 90);
            path.AddArc(new Rectangle(Width - radius, 0, radius, radius), -90, 90);
            path.AddArc(new Rectangle(Width - radius, Height - radius, radius, radius), 0, 90);
            path.AddArc(new Rectangle(0, Height - radius, radius, radius), 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }

        private void SnapToBottomRight()
        {
            var scr = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(scr.Right - Width - 12, scr.Bottom - Height - 12);
            SavePosition();
        }

        private void SnapToTopRight()
        {
            var scr = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(scr.Right - Width - 12, scr.Top + 12);
            SavePosition();
        }

        private void SavePosition()
        {
            try
            {
                var st = new PillState { X = Left, Y = Top };
                System.IO.File.WriteAllText(_stateFile, JsonSerializer.Serialize(st));
            }
            catch { /* ignore */ }
        }

        private void LoadPosition()
        {
            try
            {
                if (System.IO.File.Exists(_stateFile))
                {
                    var st = JsonSerializer.Deserialize<PillState>(System.IO.File.ReadAllText(_stateFile));
                    if (st != null)
                    {
                        Location = new Point(st.X, st.Y);
                        return;
                    }
                }
            }
            catch { /* ignore */ }
            SnapToBottomRight();
        }

        private sealed class PillState { public int X { get; set; } public int Y { get; set; } }

        /// <summary>
        /// Update text and color according to time left.
        /// </summary>
        public void SetDisplay(TimeSpan left, DateTimeOffset allowedUntil)
        {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;

            string txt;
            if (left.TotalHours >= 1)
                txt = $"{(int)left.TotalHours:D2}:{left.Minutes:D2}:{left.Seconds:D2}";
            else
                txt = $"{left.Minutes:D2}:{left.Seconds:D2}";

            _label.Text = txt;

            // Tooltip shows absolute time
            var local = allowedUntil.LocalDateTime;
            var abs = $"{local:HH:mm:ss}";
            _label.AccessibleDescription = abs;
            try { this.Invoke(new Action(() => this.Text = "Time left")); } catch { /* ignore */ } // keep form title harmless

            // Background severity
            var mins = left.TotalMinutes;
            if (mins <= 1)
                BackColor = Color.FromArgb(200, 30, 30);       // red
            else if (mins <= 5)
                BackColor = Color.FromArgb(230, 120, 20);      // orange
            else if (mins <= 15)
                BackColor = Color.FromArgb(240, 180, 20);      // amber
            else
                BackColor = Color.FromArgb(40, 160, 60);       // green
        }
    }
}
