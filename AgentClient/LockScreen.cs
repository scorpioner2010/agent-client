using System;
using System.Threading;
using System.Windows.Forms;

namespace AgentClient
{
    public static class LockScreen
    {
        private static readonly object Sync = new();
        private static Thread? _uiThread;
        private static LockForm? _form;

        public static bool IsShown { get; private set; }

        public static void Show(string password, string message = "Your time has expired")
        {
            lock (Sync)
            {
                if (IsShown) return;
                IsShown = true;

                _uiThread = new Thread(() =>
                {
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    _form = new LockForm(password, message);
                    _form.FormClosed += (_, __) =>
                    {
                        IsShown = false;
                        _form = null;
                        Application.ExitThread();
                    };
                    Application.Run(_form);
                });

                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.IsBackground = true;
                _uiThread.Start();
            }
        }

        public static void Hide()
        {
            lock (Sync)
            {
                if (!IsShown || _form == null) return;
                try { _form.Invoke(new Action(() => _form.Close())); } catch { /* ignore */ }
            }
        }
    }
}