using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AgentClient
{
    public static class PillTimer
    {
        private static readonly object Sync = new();
        private static Thread? _uiThread;
        private static PillTimerForm? _form;
        private static NotifyIcon? _tray;
        private static int _lastBucket = 3;

        public static bool IsRunning { get; private set; }

        public static void Start()
        {
            lock (Sync)
            {
                if (IsRunning) return;
                IsRunning = true;

                _uiThread = new Thread(() =>
                {
                    try
                    {
                        // Per-monitor DPI awareness (fixes scaling/position across different resolutions)
                        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);

                        _form = new PillTimerForm();
                        _form.Show();

                        _tray = new NotifyIcon
                        {
                            Icon = System.Drawing.SystemIcons.Information,
                            Visible = true,
                            Text = "Time left"
                        };

                        var menu = new ContextMenuStrip();
                        menu.Items.Add("Show timer", null, (_, __) => _form?.Invoke(new Action(() => { _form!.Show(); _form!.Activate(); })));
                        menu.Items.Add("Hide timer", null, (_, __) => _form?.Invoke(new Action(() => _form!.Hide())));
                        menu.Items.Add("Reset position", null, (_, __) => _form?.Invoke(new Action(() => _form!.ContextMenuStrip!.Items[0].PerformClick())));
                        _tray.ContextMenuStrip = menu;

                        Application.Run();
                    }
                    catch (Exception ex)
                    {
                        try { File.AppendAllText("agent_pill.log", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} UI error: {ex}\r\n"); } catch { }
                    }
                    finally
                    {
                        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                    }
                });

                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.IsBackground = true;
                _uiThread.Start();
            }
        }

        public static void SetVisible(bool visible)
        {
            lock (Sync)
            {
                if (_form == null) return;
                try
                {
                    _form.Invoke(new Action(() =>
                    {
                        if (visible) _form.Show();
                        else _form.Hide();
                    }));
                }
                catch { /* ignore */ }
            }
        }

        public static void Update(TimeSpan left, DateTimeOffset allowedUntil)
        {
            lock (Sync)
            {
                if (_form != null)
                {
                    try { _form.Invoke(new Action(() => _form.SetDisplay(left, allowedUntil))); } catch { }
                }

                if (_tray != null)
                {
                    try
                    {
                        string hhmmss = left.TotalHours >= 1
                            ? $"{(int)left.TotalHours:D2}:{left.Minutes:D2}:{left.Seconds:D2}"
                            : $"{left.Minutes:D2}:{left.Seconds:D2}";
                        _tray.Text = $"Time left: {hhmmss}";

                        int bucket = left.TotalMinutes switch
                        {
                            < 1 => 0, < 5 => 1, < 15 => 2, _ => 3
                        };

                        if (bucket < _lastBucket)
                        {
                            string msg = bucket switch
                            {
                                2 => "15 minutes left",
                                1 => "5 minutes left",
                                0 => "Less than 1 minute!",
                                _ => ""
                            };
                            if (!string.IsNullOrEmpty(msg))
                            {
                                _tray.BalloonTipTitle = "Time warning";
                                _tray.BalloonTipText = msg;
                                _tray.ShowBalloonTip(3000);
                            }
                        }
                        _lastBucket = bucket;
                    }
                    catch { /* ignore */ }
                }
            }
        }
    }
}
