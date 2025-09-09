using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgentClient
{
    /// <summary>
    /// Global low-level keyboard hook to block Alt+Tab, Alt+Esc, Alt+F4,
    /// Win keys, Win+Tab, Ctrl+Esc, Ctrl+Shift+Esc while lock screen is shown.
    /// </summary>
    public static class KeyboardBlocker
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelKeyboardProc? _proc;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private static bool IsDown(Keys k) => (GetKeyState((int)k) & 0x8000) != 0;

        public static void Install()
        {
            if (_hookId != IntPtr.Zero) return;
            _proc = HookCallback;
            using var cur = System.Diagnostics.Process.GetCurrentProcess();
            using var mod = cur.MainModule!;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
        }

        public static void Uninstall()
        {
            if (_hookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _proc = null;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    var vk = (Keys)data.vkCode;

                    bool alt = IsDown(Keys.Menu);
                    bool ctrl = IsDown(Keys.ControlKey);
                    bool shift = IsDown(Keys.ShiftKey);

                    // Block Windows keys
                    if (vk == Keys.LWin || vk == Keys.RWin) return (IntPtr)1;

                    // Block combos
                    if (alt && (vk == Keys.Tab || vk == Keys.Escape || vk == Keys.F4)) return (IntPtr)1; // Alt+Tab/Alt+Esc/Alt+F4
                    if (ctrl && vk == Keys.Escape) return (IntPtr)1;                                     // Ctrl+Esc
                    if ((vk == Keys.Tab) && (IsDown(Keys.LWin) || IsDown(Keys.RWin))) return (IntPtr)1;  // Win+Tab
                    if (ctrl && shift && vk == Keys.Escape) return (IntPtr)1;                            // Ctrl+Shift+Esc (TaskMgr)
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}
