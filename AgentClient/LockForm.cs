using System;
using System.Drawing;
using System.Windows.Forms;

namespace AgentClient
{
    /// <summary>
    /// Fullscreen top-most lock window with password box.
    /// Hidden from Alt+Tab and ignores Alt+F4/system close.
    /// Black theme. Supports online server password and optional offline override.
    /// Backward-compatible: old constructor and Unlocked event still work.
    /// </summary>
    public class LockForm : Form
    {
        // THEME
        private static readonly Color BgColor = Color.Black;
        private static readonly Color FgColor = Color.White;
        private static readonly Color AccentColor = Color.FromArgb(40, 160, 60);

        // Passwords / mode
        private readonly string _serverPassword;
        private readonly bool _allowOfflineOverride;
        private readonly string _offlineOverridePassword;

        private readonly TextBox _tb;
        private readonly Button _btn;
        private bool _allowClose = false;

        // Events
        public event Action? Unlocked; // legacy (no args)
        public event Action<UnlockKind>? UnlockedWithKind; // new (tells which path unlocked)

        /// <summary>
        /// New recommended constructor.
        /// </summary>
        public LockForm(
            string serverPassword,
            bool allowOfflineOverride,
            string offlineOverridePassword = "1111",
            string message = "Access is locked")
        {
            _serverPassword = serverPassword ?? string.Empty;
            _allowOfflineOverride = allowOfflineOverride;
            _offlineOverridePassword = offlineOverridePassword ?? "1111";

            // Window chrome
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = BgColor;

            // Title
            var title = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 36, FontStyle.Bold),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 60, 0, 20),
                ForeColor = FgColor,
                BackColor = Color.Transparent
            };

            var lbl = new Label
            {
                Text = "Admin password:",
                Font = new Font("Segoe UI", 16),
                AutoSize = true,
                Margin = new Padding(0, 0, 10, 0),
                ForeColor = FgColor,
                BackColor = Color.Transparent
            };

            _tb = new TextBox
            {
                UseSystemPasswordChar = true,
                Font = new Font("Segoe UI", 18),
                Width = 380,
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = FgColor,
                BorderStyle = BorderStyle.FixedSingle
            };

            _btn = new Button
            {
                Text = "Unlock",
                Font = new Font("Segoe UI", 14),
                AutoSize = true,
                BackColor = AccentColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btn.FlatAppearance.BorderSize = 0;

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Anchor = AnchorStyles.None,
                BackColor = Color.Transparent
            };
            row.Controls.Add(lbl);
            row.Controls.Add(_tb);
            row.Controls.Add(_btn);

            var mid = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            mid.Controls.Add(row);
            row.Location = new Point((mid.Width - row.Width) / 2, (mid.Height - row.Height) / 2);
            mid.Resize += (_, __) =>
            {
                row.Location = new Point((mid.Width - row.Width) / 2, (mid.Height - row.Height) / 2);
            };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = BgColor };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(mid, 0, 1);
            layout.Controls.Add(new Panel { Height = 40, Dock = DockStyle.Top, BackColor = BgColor }, 0, 2);

            Controls.Add(layout);

            _btn.Click += (_, __) => TryUnlock();
            _tb.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { TryUnlock(); e.Handled = true; } };

            // Block Esc / Alt / Ctrl within our window
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape || e.Alt || e.Control) e.Handled = true;
            };
        }

        /// <summary>
        /// Backward-compatible old constructor (kept so existing LockScreen code builds).
        /// No offline override via this path. Black theme applied.
        /// </summary>
        public LockForm(string password, string message = "Your time has expired")
            : this(serverPassword: password, allowOfflineOverride: false, offlineOverridePassword: "1111", message: message)
        {
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Cursor.Hide();
            Activate();
            _tb.Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Cursor.Show();
            base.OnFormClosed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Ignore Alt+F4 / system close unless we explicitly allow it
            if (!_allowClose)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        private void TryUnlock()
        {
            var input = _tb.Text ?? string.Empty;

            // Determine which password matched (if any)
            UnlockKind? kind = null;

            // server password has priority
            if (!string.IsNullOrEmpty(_serverPassword) &&
                string.Equals(input, _serverPassword, StringComparison.Ordinal))
            {
                kind = UnlockKind.OnlinePassword;
            }
            else if (_allowOfflineOverride &&
                     string.Equals(input, _offlineOverridePassword, StringComparison.Ordinal))
            {
                kind = UnlockKind.OfflineOverride;
            }

            if (kind.HasValue)
            {
                // Fire both events (new + legacy)
                UnlockedWithKind?.Invoke(kind.Value);
                Unlocked?.Invoke();

                _allowClose = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _tb.Clear();
                _tb.Focus();
                System.Media.SystemSounds.Beep.Play();
            }
        }
    }

    public enum UnlockKind
    {
        OnlinePassword = 1,
        OfflineOverride = 2
    }
}
