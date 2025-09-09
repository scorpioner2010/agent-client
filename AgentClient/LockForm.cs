using System.Drawing;
using System.Windows.Forms;

namespace AgentClient
{
    /// <summary>
    /// Fullscreen top-most lock window with password + Unlock / Power off.
    /// Hidden from Alt+Tab and ignores Alt+F4/system close.
    /// </summary>
    public class LockForm : Form
    {
        private readonly string _serverPassword;
        private readonly bool _allowOfflineOverride;
        private readonly string _offlinePassword;

        private readonly TextBox _tb;
        private readonly Button _btnUnlock;
        private readonly Button _btnShutdown;
        private bool _allowClose = false;

        public LockForm(
            string serverPassword,
            string message,
            bool allowOfflineOverride,
            string offlinePassword)
        {
            _serverPassword = serverPassword ?? "";
            _allowOfflineOverride = allowOfflineOverride;
            _offlinePassword = offlinePassword ?? "";

            // Window
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.Black;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;

            // Title
            var title = new Label
            {
                Text = string.IsNullOrWhiteSpace(message) ? "Access is locked" : message,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 36, FontStyle.Bold),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 60, 0, 20)
            };

            // Row: label + textbox + unlock
            var lbl = new Label
            {
                Text = "Admin password:",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 16),
                AutoSize = true,
                Margin = new Padding(0, 0, 12, 0)
            };

            _tb = new TextBox
            {
                UseSystemPasswordChar = true,
                Font = new Font("Segoe UI", 18),
                Width = 420,
                BackColor = Color.White,
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle,
                TabIndex = 0
            };

            _btnUnlock = new Button
            {
                Text = "Unlock",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(160, 48),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 148, 75), // green
                ForeColor = Color.White,
                Margin = new Padding(12, 0, 0, 0),
                Cursor = Cursors.Hand,
                TabIndex = 1
            };
            _btnUnlock.FlatAppearance.BorderSize = 0;

            // Enter triggers Unlock
            AcceptButton = _btnUnlock;

            var rowPassword = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            rowPassword.Controls.Add(lbl);
            rowPassword.Controls.Add(_tb);
            rowPassword.Controls.Add(_btnUnlock);

            // Row: big red shutdown button
            _btnShutdown = new Button
            {
                Text = "Power off",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(240, 54),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 48, 48), // red
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 24, 0, 0),
                TabIndex = 2
            };
            _btnShutdown.FlatAppearance.BorderSize = 0;

            var rowShutdown = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            rowShutdown.Controls.Add(_btnShutdown);

            // Stack (vertical) = centered block
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            stack.Controls.Add(rowPassword);
            stack.Controls.Add(rowShutdown);

            // Center container
            var mid = new Panel { Dock = DockStyle.Fill };
            mid.Controls.Add(stack);

            void CenterStack()
            {
                var pref = stack.PreferredSize;
                stack.Location = new Point(
                    (mid.Width - pref.Width) / 2,
                    (mid.Height - pref.Height) / 2
                );
            }
            mid.Resize += (_, __) => CenterStack();
            mid.HandleCreated += (_, __) => CenterStack();

            // Page layout
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(mid, 0, 1);
            layout.Controls.Add(new Panel { Height = 40, Dock = DockStyle.Top }, 0, 2);
            Controls.Add(layout);

            // Events
            _btnUnlock.Click += (_, __) => TryUnlock();
            _tb.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { TryUnlock(); e.Handled = true; } };
            _btnShutdown.Click += (_, __) => TryShutdown();

            // block Esc/Alt/Ctrl inside this window (global blockers — окремо)
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape || e.Alt || e.Control) e.Handled = true;
            };
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

        protected override void OnShown(System.EventArgs e)
        {
            base.OnShown(e);
            // НЕ ховаємо курсор — щоб можна було клікнути Power off
            Activate();
            _tb.Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // курсор не чіпаємо
            base.OnFormClosed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
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
            var entered = _tb.Text ?? "";

            if (entered == _serverPassword)
            {
                _allowClose = true;
                LockScreen.RaiseUnlocked(UnlockKind.OnlinePassword);
                Close();
                return;
            }

            if (_allowOfflineOverride && entered == _offlinePassword)
            {
                _allowClose = true;
                LockScreen.RaiseUnlocked(UnlockKind.OfflineOverride);
                Close();
                return;
            }

            _tb.Clear();
            _tb.Focus();
            System.Media.SystemSounds.Beep.Play();
        }

        private void TryShutdown()
        {
            ForceShutdown.Now(); // гарантоване вимкнення (з фолбеком)
        }
    }
}
