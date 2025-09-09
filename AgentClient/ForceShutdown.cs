using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentClient
{
    /// <summary>
    /// Forces system shutdown. Tries to enable SeShutdownPrivilege and call ExitWindowsEx.
    /// Falls back to "shutdown /s /t 0 /f".
    /// </summary>
    public static class ForceShutdown
    {
        // --- WinAPI ---
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        private const uint EWX_LOGOFF = 0x00000000;
        private const uint EWX_SHUTDOWN = 0x00000001;
        private const uint EWX_REBOOT = 0x00000002;
        private const uint EWX_FORCE = 0x00000004;
        private const uint EWX_POWEROFF = 0x00000008;
        private const uint EWX_FORCEIFHUNG = 0x00000010;

        private static void EnablePrivilege(string name)
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle,
                    TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
                return;

            try
            {
                if (!LookupPrivilegeValue(null, name, out var luid))
                    return;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                try { Marshal.FreeHGlobal(token); } catch { /* ignore */ }
            }
        }

        public static void Now()
        {
            try
            {
                // 1) пробуємо через WinAPI з привілеями
                EnablePrivilege("SeShutdownPrivilege");
                if (ExitWindowsEx(EWX_SHUTDOWN | EWX_POWEROFF | EWX_FORCE | EWX_FORCEIFHUNG, 0))
                    return;
            }
            catch { /* ignore */ }

            try
            {
                // 2) фолбек: стандартна утиліта
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/s /t 0 /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch
            {
                // останній шанс — нічого
            }
        }
    }
}
