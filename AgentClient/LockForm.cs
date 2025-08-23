using System.Drawing;
using System.Windows.Forms;

namespace AgentClient
{
    // Повноекранне топове вікно з полем пароля
    public class LockForm : Form
    {
        private readonly string _password;
        private readonly TextBox _tb;
        private readonly Button _btn;

        public LockForm(string password, string message = "Ваш час закінчився")
        {
            _password = password;

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Color.White;

            var title = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 36, FontStyle.Bold),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 60, 0, 20)
            };

            var lbl = new Label
            {
                Text = "Адмін пароль:",
                Font = new Font("Segoe UI", 16),
                AutoSize = true,
                Margin = new Padding(0, 0, 10, 0)
            };

            _tb = new TextBox
            {
                UseSystemPasswordChar = true,
                Font = new Font("Segoe UI", 18),
                Width = 380
            };

            _btn = new Button
            {
                Text = "Розблокувати",
                Font = new Font("Segoe UI", 14),
                AutoSize = true
            };

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Anchor = AnchorStyles.None
            };
            row.Controls.Add(lbl);
            row.Controls.Add(_tb);
            row.Controls.Add(_btn);

            var mid = new Panel { Dock = DockStyle.Fill };
            mid.Controls.Add(row);
            row.Location = new Point((mid.Width - row.Width) / 2, (mid.Height - row.Height) / 2);
            mid.Resize += (_, __) =>
            {
                row.Location = new Point((mid.Width - row.Width) / 2, (mid.Height - row.Height) / 2);
            };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(mid, 0, 1);
            layout.Controls.Add(new Panel { Height = 40, Dock = DockStyle.Top }, 0, 2);

            Controls.Add(layout);

            _btn.Click += (_, __) => TryUnlock();
            _tb.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { TryUnlock(); e.Handled = true; } };

            // блокуємо ESC/Alt/Ctrl у нашому вікні
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape || e.Alt || e.Control) e.Handled = true;
            };
        }

        protected override void OnShown(System.EventArgs e)
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

        private void TryUnlock()
        {
            if (_tb.Text == _password)
            {
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
}
