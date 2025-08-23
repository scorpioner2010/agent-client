using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentClient
{
    /// <summary>
    /// Draggable pill showing time left. Anchored to screen corner with margins (resolution/DPI safe).
    /// Hidden from Alt+Tab.
    /// </summary>
    public sealed class PillTimerForm : Form
    {
        private readonly Label _label;
        private Point _dragStart;
        private bool _dragging;
        private readonly string _stateFile = "pill_state.json";

        private readonly int _w = 220;
        private readonly int _h = 48;

        private enum AnchorCorner { BottomRight, TopRight, BottomLeft, TopLeft }
        private AnchorCorner _anchor = AnchorCorner.BottomRight;
        private int _marginX = 12; // px from anchor side horizontally
        private int _marginY = 12; // px from anchor side vertically

        public PillTimerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;
            Size = new Size(_w, _h);
            SetRoundedRegion();

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
                    SnapAnchorAndMarginsToCurrentPosition();
                    SaveState();
                }
            };

            // Context menu
            ContextMenuStrip = new ContextMenuStrip();
            ContextMenuStrip.Items.Add("Snap to bottom-right", null, (_, __) =>
            {
                _anchor = AnchorCorner.BottomRight; _marginX = 12; _marginY = 12; ApplyAnchor(); SaveState();
            });
            ContextMenuStrip.Items.Add("Snap to top-right", null, (_, __) =>
            {
                _anchor = AnchorCorner.TopRight; _marginX = 12; _marginY = 12; ApplyAnchor(); SaveState();
            });
            ContextMenuStrip.Items.Add("Snap to bottom-left", null, (_, __) =>
            {
                _anchor = AnchorCorner.BottomLeft; _marginX = 12; _marginY = 12; ApplyAnchor(); SaveState();
            });
            ContextMenuStrip.Items.Add("Snap to top-left", null, (_, __) =>
            {
                _anchor = AnchorCorner.TopLeft; _marginX = 12; _marginY = 12; ApplyAnchor(); SaveState();
            });

            // Load persisted anchor/margins and apply
            LoadState();
            ApplyAnchor();
            EnsureOnScreen();

            // Re-apply when display settings change (resolution/monitor layout)
            SystemEvents.DisplaySettingsChanged += (_, __) => { try { ApplyAnchor(); EnsureOnScreen(); } catch { } };
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

        private static Rectangle WorkingUnion()
        {
            Rectangle? r = null;
            foreach (var s in Screen.AllScreens)
                r = r == null ? s.WorkingArea : Rectangle.Union(r.Value, s.WorkingArea);
            return r ?? Screen.PrimaryScreen!.WorkingArea;
        }

        private Rectangle CurrentScreenWorking()
        {
            // Anchor to the screen where the center of the form is
            var center = new Point(Location.X + Width / 2, Location.Y + Height / 2);
            return Screen.FromPoint(center).WorkingArea;
        }

        private void ApplyAnchor()
        {
            var wa = CurrentScreenWorking();
            int x, y;
            switch (_anchor)
            {
                case AnchorCorner.BottomRight:
                    x = wa.Right - Width - _marginX;
                    y = wa.Bottom - Height - _marginY;
                    break;
                case AnchorCorner.TopRight:
                    x = wa.Right - Width - _marginX;
                    y = wa.Top + _marginY;
                    break;
                case AnchorCorner.BottomLeft:
                    x = wa.Left + _marginX;
                    y = wa.Bottom - Height - _marginY;
                    break;
                case AnchorCorner.TopLeft:
                    x = wa.Left + _marginX;
                    y = wa.Top + _marginY;
                    break;
                default:
                    x = wa.Right - Width - 12;
                    y = wa.Bottom - Height - 12;
                    break;
            }
            Location = new Point(x, y);
        }

        private void SnapAnchorAndMarginsToCurrentPosition()
        {
            var wa = CurrentScreenWorking();

            // Choose nearest horizontal side
            int distLeft = Math.Abs(Left - wa.Left);
            int distRight = Math.Abs(wa.Right - (Left + Width));
            bool useLeft = distLeft <= distRight;

            // Choose nearest vertical side
            int distTop = Math.Abs(Top - wa.Top);
            int distBottom = Math.Abs(wa.Bottom - (Top + Height));
            bool useTop = distTop <= distBottom;

            if (!useLeft && !useTop) _anchor = AnchorCorner.BottomRight;
            else if (!useLeft && useTop) _anchor = AnchorCorner.TopRight;
            else if (useLeft && !useTop) _anchor = AnchorCorner.BottomLeft;
            else _anchor = AnchorCorner.TopLeft;

            // Margins from selected sides
            _marginX = useLeft ? (Left - wa.Left) : (wa.Right - (Left + Width));
            _marginY = useTop ? (Top - wa.Top) : (wa.Bottom - (Top + Height));

            // Re-apply to normalize exact position
            ApplyAnchor();
        }

        private void EnsureOnScreen()
        {
            var union = WorkingUnion();
            var rect = new Rectangle(Location, Size);
            if (!rect.IntersectsWith(union))
            {
                // fallback to default BR
                _anchor = AnchorCorner.BottomRight;
                _marginX = 12; _marginY = 12;
                ApplyAnchor();
            }
        }

        private sealed class PillState
        {
            public string Anchor { get; set; } = "BottomRight";
            public int MarginX { get; set; } = 12;
            public int MarginY { get; set; } = 12;
        }

        private void SaveState()
        {
            try
            {
                var st = new PillState
                {
                    Anchor = _anchor.ToString(),
                    MarginX = _marginX,
                    MarginY = _marginY
                };
                System.IO.File.WriteAllText(_stateFile, JsonSerializer.Serialize(st));
            }
            catch { /* ignore */ }
        }

        private void LoadState()
        {
            try
            {
                if (System.IO.File.Exists(_stateFile))
                {
                    var st = JsonSerializer.Deserialize<PillState>(System.IO.File.ReadAllText(_stateFile));
                    if (st != null)
                    {
                        if (Enum.TryParse<AnchorCorner>(st.Anchor, out var a)) _anchor = a;
                        _marginX = st.MarginX;
                        _marginY = st.MarginY;
                        return;
                    }
                }
            }
            catch { /* ignore */ }
            _anchor = AnchorCorner.BottomRight; _marginX = 12; _marginY = 12;
        }

        public void SetDisplay(TimeSpan left, DateTimeOffset allowedUntil)
        {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            string txt = left.TotalHours >= 1
                ? $"{(int)left.TotalHours:D2}:{left.Minutes:D2}:{left.Seconds:D2}"
                : $"{left.Minutes:D2}:{left.Seconds:D2}";
            _label.Text = txt;

            var mins = left.TotalMinutes;
            if (mins <= 1) BackColor = Color.FromArgb(200, 30, 30);
            else if (mins <= 5) BackColor = Color.FromArgb(230, 120, 20);
            else if (mins <= 15) BackColor = Color.FromArgb(240, 180, 20);
            else BackColor = Color.FromArgb(40, 160, 60);
        }
    }
}
