using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentClient
{
    internal static class Program
    {
        // -----------------------------
        // SERVER BASES + TOGGLE
        // Завжди є тестове поле (LOCAL).
        // Міняй тільки UseLocal: true -> localhost, false -> PROD.
        private const string ServerBaseLocal = "https://localhost:44318";                 // TEST
        private const string ServerBaseProd  = "https://pc-admin-server.onrender.com";    // PROD
        private const bool UseLocal = false; // <-- вистави true для локального тесту
        private static readonly string ServerBase = UseLocal ? ServerBaseLocal : ServerBaseProd;
        // -----------------------------
        // Endpoints
        private static readonly Uri StatusEndpoint = new Uri($"{ServerBase}/api/agent/status");
        private static readonly Uri UnlockEndpoint = new Uri($"{ServerBase}/api/agent/unlock");

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

        // --- Policy state (from server) ---
        private static DateTimeOffset _allowedUntil = DateTimeOffset.MaxValue;
        private static DateTimeOffset _lastContactOk = DateTimeOffset.MinValue;
        private static int _manualUnlockGraceMinutes = 60;         // "Password time" from server
        private static string _serverUnlockPassword = "admin123";  // delivered by server in /status

        // --- Local override (used only when offline) ---
        private static DateTimeOffset? _localUnlockUntil = null;
        private const string OfflineOverridePassword = "1111";     // if offline, fully unlock
        private static readonly TimeSpan OfflineOverrideDuration = TimeSpan.FromHours(10);

        // --- Misc ---
        private const string LogFile = "agent.log";
        private static readonly object LogLock = new();
        private static string _machineName = "";
        private static string _os = "";

        private static void Log(string msg)
        {
            try { lock (LogLock) File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\r\n"); }
            catch { /* ignore */ }
        }

        private static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            { try { File.WriteAllText("agent_crash.log", e.ExceptionObject?.ToString() ?? "null"); } catch {} };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            { try { File.AppendAllText("agent_crash.log", "\n" + e.Exception); } catch {} e.SetObserved(); };

            Log("Agent starting... ServerBase=" + ServerBase);

            // Start pill (always-on-top timer) + tray
            PillTimer.Start();

            // Subscribe to lock-screen events: online unlock or offline override
            LockScreen.OnUnlockedKind += kind => _ = HandleUnlockedAsync(kind);

            _machineName = Environment.MachineName;
            _os = RuntimeInformation.OSDescription;

            while (true)
            {
                var now = DateTimeOffset.Now;
                var uptimeSeconds = Environment.TickCount64 / 1000;

                var payload = new
                {
                    Machine = _machineName,
                    OS = _os,
                    UptimeSec = uptimeSeconds,
                    Time = now.ToString("O")
                };

                string json = JsonSerializer.Serialize(payload);
                Log("[Status] " + json);

                bool sent = false;
                bool requireLock = false;

                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var resp = await Http.PostAsync(StatusEndpoint, content);
                    var body = await resp.Content.ReadAsStringAsync();
                    Log($"[Server] {(int)resp.StatusCode} {body}");

                    if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body))
                    {
                        using var doc = JsonDocument.Parse(body);

                        // --- Policy ---
                        if (doc.RootElement.TryGetProperty("policy", out var policy))
                        {
                            if (policy.TryGetProperty("allowedUntil", out var auEl) &&
                                DateTimeOffset.TryParse(auEl.GetString(), out var au))
                            {
                                _allowedUntil = au;
                            }
                            if (policy.TryGetProperty("requireLock", out var rlEl))
                            {
                                requireLock = rlEl.GetBoolean();
                            }
                            if (policy.TryGetProperty("manualUnlockGraceMinutes", out var gEl))
                            {
                                var v = gEl.GetInt32();
                                if (v < 0) v = 0; if (v > 240) v = 240;
                                _manualUnlockGraceMinutes = v;
                            }
                            if (policy.TryGetProperty("unlockPassword", out var pwdEl))
                            {
                                _serverUnlockPassword = pwdEl.GetString() ?? "";
                            }
                        }

                        // --- One-shot command from server ---
                        if (doc.RootElement.TryGetProperty("command", out var cmdEl) &&
                            cmdEl.ValueKind == JsonValueKind.String)
                        {
                            var cmd = cmdEl.GetString();
                            if (!string.IsNullOrWhiteSpace(cmd))
                            {
                                Log("[Command] received: " + cmd);
                                _ = ExecuteCommandAsync(cmd!); // fire & forget
                            }
                        }

                        _lastContactOk = now;
                        sent = true;
                        _localUnlockUntil = null; // when back online, local override is cleared
                    }
                }
                catch (Exception ex)
                {
                    Log("[Send error] " + ex.Message);
                }

                if (!sent)
                {
                    var offlineFor = now - _lastContactOk;
                    Log($"[Offline] approx {offlineFor.TotalSeconds:F0}s");
                }

                // ---- Lock decision ----
                bool localOverrideActive = _localUnlockUntil.HasValue && now < _localUnlockUntil.Value;
                bool shouldLock = sent
                    ? (requireLock || now >= _allowedUntil)          // ONLINE: server rules
                    : (!localOverrideActive && now >= _allowedUntil);// OFFLINE: allow local override

                if (shouldLock && !LockScreen.IsShown)
                {
                    Log("[Policy] SHOW lock screen");
                    var allowOfflineOverride = !sent; // allow 1111 only if offline now
                    LockScreen.Show(
                        serverPassword: _serverUnlockPassword,
                        allowOfflineOverride: allowOfflineOverride,
                        message: "Access is locked",
                        offlineOverridePassword: OfflineOverridePassword
                    );
                    PillTimer.SetVisible(false);
                }
                else if (!shouldLock && LockScreen.IsShown)
                {
                    Log("[Policy] HIDE lock screen");
                    LockScreen.Hide();
                    PillTimer.SetVisible(true);
                }

                // --- Update pill + tray ---
                var left = _allowedUntil - now;
                PillTimer.Update(left, _allowedUntil);

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        /// <summary>
        /// Reacts to unlock path: server password (online) or offline override.
        /// </summary>
        private static async Task HandleUnlockedAsync(UnlockKind kind)
        {
            try
            {
                var now = DateTimeOffset.Now;

                if (kind == UnlockKind.OnlinePassword)
                {
                    // Ask server to grant "Password time" from now
                    Log("[Unlock] Online password entered → POST /unlock");
                    try
                    {
                        var req = new
                        {
                            Machine = _machineName,
                            Password = _serverUnlockPassword
                        };
                        using var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
                        var resp = await Http.PostAsync(UnlockEndpoint, content);
                        var body = await resp.Content.ReadAsStringAsync();
                        Log($"[Unlock] Server response {(int)resp.StatusCode} {body}");

                        if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body))
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("policy", out var p) &&
                                p.TryGetProperty("allowedUntil", out var auEl) &&
                                DateTimeOffset.TryParse(auEl.GetString(), out var au))
                            {
                                _allowedUntil = au;
                            }
                            _localUnlockUntil = null; // server took over
                            return;
                        }

                        // Fallback if unlock call failed:
                        Log("[Unlock] Server unlock failed, applying local grace fallback");
                        _localUnlockUntil = now.AddMinutes(_manualUnlockGraceMinutes);
                        _allowedUntil = _localUnlockUntil.Value;
                    }
                    catch (Exception ex)
                    {
                        Log("[Unlock] Error calling /unlock: " + ex.Message);
                        _localUnlockUntil = now.AddMinutes(_manualUnlockGraceMinutes);
                        _allowedUntil = _localUnlockUntil.Value;
                    }
                }
                else if (kind == UnlockKind.OfflineOverride)
                {
                    // Full offline unlock for 10 hours
                    Log("[Unlock] Offline override accepted → 10h local grace");
                    _localUnlockUntil = now.Add(OfflineOverrideDuration);
                    _allowedUntil = _localUnlockUntil.Value;
                }
            }
            catch (Exception ex)
            {
                Log("[Unlock] Handler error: " + ex.Message);
            }
        }

        /// <summary>
        /// Execute one-shot command received from server.
        /// </summary>
        private static async Task ExecuteCommandAsync(string cmd)
        {
            cmd = cmd.Trim().ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "sleep":
                        Log("[Command] executing sleep");
                        await SleepAsync();
                        break;

                    case "shutdown":
                        Log("[Command] executing shutdown (10s)");
                        await ShutdownAsync();
                        break;

                    default:
                        Log("[Command] unknown: " + cmd);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("[Command] error: " + ex.Message);
            }
        }

        [SupportedOSPlatform("windows")]
        private static Task SleepAsync()
        {
            try
            {
                Application.SetSuspendState(PowerState.Suspend, true, false);
            }
            catch
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "rundll32",
                        Arguments = "powrprof.dll,SetSuspendState 0,1,0",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Process.Start(psi);
                }
                catch (Exception ex2)
                {
                    Log("[Sleep fallback] " + ex2.Message);
                }
            }
            return Task.CompletedTask;
        }

        [SupportedOSPlatform("windows")]
        private static Task ShutdownAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/s /t 10 /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log("[Shutdown] " + ex.Message);
            }
            return Task.CompletedTask;
        }
    }
}
