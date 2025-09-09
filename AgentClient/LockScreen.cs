using System;
using System.Threading;
using System.Windows.Forms;

namespace AgentClient
{
    public enum UnlockKind
    {
        OnlinePassword,
        OfflineOverride
    }

    public static class LockScreen
    {
        private static readonly object Sync = new();
        private static Thread? _uiThread;
        private static LockForm? _form;

        public static bool IsShown { get; private set; }

        public static event Action<UnlockKind>? OnUnlockedKind;

        public static void Show(
            string serverPassword,
            string message,
            bool allowOfflineOverride,
            string offlineOverridePassword)
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

                    _form = new LockForm(
                        serverPassword,
                        message,
                        allowOfflineOverride,
                        offlineOverridePassword);

                    _form.FormClosed += (_, __) =>
                    {
                        IsShown = false;
                        _form = null;
                        // Якщо додавав блокери клавіш/вотчер — вимикай тут:
                        try { KeyboardBlocker.Uninstall(); } catch { }
                        try { TaskManagerWatcher.Stop(); } catch { }
                        Application.ExitThread();
                    };

                    // Якщо додавав блокери клавіш/вотчер — вмикай тут:
                    try { KeyboardBlocker.Install(); } catch { }
                    try { TaskManagerWatcher.Start(); } catch { }

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
                try
                {
                    _form.Invoke(new Action(() => _form.ForceClose()));
                }
                catch { /* ignore */ }
            }
        }

        internal static void RaiseUnlocked(UnlockKind kind)
            => OnUnlockedKind?.Invoke(kind);
    }
}
