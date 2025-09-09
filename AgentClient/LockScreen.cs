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

        // Події:
        // 1) стара (зворотна сумісність)
        public static event Action? OnUnlocked;
        // 2) нова — з інформацією ЯК розблокували
        public static event Action<UnlockKind>? OnUnlockedKind;

        /// <summary>
        /// Новий рекомендований Show: передаємо поточний серверний пароль і прапорець офлайн-оверрайду.
        /// offlineOverridePassword за замовчуванням "1111".
        /// </summary>
        public static void Show(string serverPassword, bool allowOfflineOverride, string message = "Access is locked", string offlineOverridePassword = "1111")
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
                        serverPassword: serverPassword,
                        allowOfflineOverride: allowOfflineOverride,
                        offlineOverridePassword: offlineOverridePassword,
                        message: message
                    );

                    // прокидуємо події нагору
                    _form.Unlocked += () => OnUnlocked?.Invoke();
                    _form.UnlockedWithKind += kind => OnUnlockedKind?.Invoke(kind);

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

        /// <summary>
        /// Старий варіант Show для зворотної сумісності (без офлайн-оверрайду).
        /// </summary>
        public static void Show(string password, string message = "Your time has expired")
            => Show(serverPassword: password, allowOfflineOverride: false, message: message, offlineOverridePassword: "1111");

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
    }
}
