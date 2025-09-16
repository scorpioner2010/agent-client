using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgentClient
{
    internal static class Program
    {
        private const string PROD_BASE = "https://pc-admin-server.onrender.com";
        private const string LOCAL_BASE = "https://localhost:44318";

        private const string ServerBase = LOCAL_BASE; // або поміняй на PROD_BASE

        private static readonly Uri StatusEndpoint = new Uri($"{ServerBase}/api/agent/status");
        private static readonly Uri UnlockEndpoint = new Uri($"{ServerBase}/api/agent/unlock");
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

        private static DateTimeOffset _allowedUntil = DateTimeOffset.MaxValue;
        private static DateTimeOffset _lastContactOk = DateTimeOffset.MinValue;
        private static int _manualUnlockGraceMinutes = 60;
        private static string _serverUnlockPassword = "7789Saurex";
        private static int _serverVolumePercent = 75; // ДЕФОЛТ 75%

        private static DateTimeOffset? _localUnlockUntil = null;
        private const string OfflineOverridePassword = "1111";
        private static readonly TimeSpan OfflineOverrideDuration = TimeSpan.FromHours(10);

        private const string LogFile = "agent.log";
        private static readonly object LogLock = new();
        private static string _machineName = "";
        private static string _os = "";

        private static void Log(string msg)
        {
            try { lock (LogLock) File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\r\n"); }
            catch { }
        }

        private static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            { try { File.WriteAllText("agent_crash.log", e.ExceptionObject?.ToString() ?? "null"); } catch { } };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            { try { File.AppendAllText("agent_crash.log", "\n" + e.Exception); } catch { } e.SetObserved(); };

            Log("Agent starting...");

            PillTimer.Start();
            LockScreen.OnUnlockedKind += kind => _ = HandleUnlockedAsync(kind);

            _machineName = Environment.MachineName;
            _os = RuntimeInformation.OSDescription;

            int currentVol = VolumeController.TryGetVolumePercent() ?? -1;

            while (true)
            {
                var now = DateTimeOffset.Now;
                var uptimeSeconds = Environment.TickCount64 / 1000;

                var payload = new
                {
                    Machine = _machineName,
                    OS = _os,
                    UptimeSec = uptimeSeconds,
                    Time = now.ToString("O"),
                    VolumePercent = currentVol
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
                        if (doc.RootElement.TryGetProperty("policy", out var policy))
                        {
                            if (policy.TryGetProperty("allowedUntil", out var auEl) &&
                                DateTimeOffset.TryParse(auEl.GetString(), out var au))
                                _allowedUntil = au;

                            if (policy.TryGetProperty("requireLock", out var rlEl))
                                requireLock = rlEl.GetBoolean();

                            if (policy.TryGetProperty("manualUnlockGraceMinutes", out var gEl))
                            {
                                var v = gEl.GetInt32();
                                if (v < 0) v = 0; if (v > 240) v = 240;
                                _manualUnlockGraceMinutes = v;
                            }

                            if (policy.TryGetProperty("unlockPassword", out var pwdEl))
                                _serverUnlockPassword = pwdEl.GetString() ?? _serverUnlockPassword;

                            if (policy.TryGetProperty("volumePercent", out var volEl))
                            {
                                var v = volEl.GetInt32();
                                _serverVolumePercent = Math.Clamp(v, 0, 100);
                            }
                        }

                        if (doc.RootElement.TryGetProperty("commands", out var commandsEl) &&
                            commandsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var c in commandsEl.EnumerateArray())
                            {
                                int type = c.TryGetProperty("type", out var t) ? t.GetInt32() : 0;
                                if (type == 2 && c.TryGetProperty("intValue", out var iv))
                                {
                                    var val = Math.Clamp(iv.GetInt32(), 0, 100);
                                    ForceApplyVolume(val);
                                    _serverVolumePercent = val;
                                }
                                else if (type == 1)
                                {
                                    ForceShutdown.Now();
                                }
                            }
                        }

                        sent = true;
                        _lastContactOk = now;
                        _localUnlockUntil = null;
                    }
                }
                catch (Exception ex)
                {
                    Log("[Send error] " + ex.Message);
                }

                if (sent)
                {
                    ForceApplyVolume(_serverVolumePercent);
                    currentVol = VolumeController.TryGetVolumePercent() ?? currentVol;
                }

                if (!sent)
                {
                    var offlineFor = now - _lastContactOk;
                    Log($"[Offline] approx {offlineFor.TotalSeconds:F0}s");
                }

                bool localOverrideActive = _localUnlockUntil.HasValue && now < _localUnlockUntil.Value;
                bool shouldLock = sent
                    ? (requireLock || now >= _allowedUntil)
                    : (!localOverrideActive && now >= _allowedUntil);

                if (shouldLock && !LockScreen.IsShown)
                {
                    Log("[Policy] SHOW lock screen]");
                    var allowOfflineOverride = !sent;
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

                var left = _allowedUntil - now;
                PillTimer.Update(left, _allowedUntil);

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        private static void ForceApplyVolume(int percent)
        {
            try
            {
                int p = Math.Clamp(percent, 0, 100);
                VolumeController.TryUnmute();
                if (VolumeController.TrySetVolumePercent(p))
                    Log($"[Volume] forced {p}%");
                else
                    Log("[Volume] force apply failed");
            }
            catch (Exception ex)
            {
                Log("[Volume] error: " + ex.Message);
            }
        }

        private static async Task HandleUnlockedAsync(UnlockKind kind)
        {
            try
            {
                var now = DateTimeOffset.Now;

                if (kind == UnlockKind.OnlinePassword)
                {
                    Log("[Unlock] Online password → POST /unlock");
                    try
                    {
                        var req = new { Machine = Environment.MachineName, Password = _serverUnlockPassword };
                        using var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
                        var resp = await Http.PostAsync(UnlockEndpoint, content);
                        var body = await resp.Content.ReadAsStringAsync();
                        Log($"[Unlock] {(int)resp.StatusCode} {body}");

                        if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body))
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("policy", out var p) &&
                                p.TryGetProperty("allowedUntil", out var auEl) &&
                                DateTimeOffset.TryParse(auEl.GetString(), out var au))
                            {
                                _allowedUntil = au;
                            }
                            _localUnlockUntil = null;
                            return;
                        }

                        _localUnlockUntil = now.AddMinutes(_manualUnlockGraceMinutes);
                        _allowedUntil = _localUnlockUntil.Value;
                    }
                    catch (Exception ex)
                    {
                        Log("[Unlock] Error: " + ex.Message);
                        _localUnlockUntil = now.AddMinutes(_manualUnlockGraceMinutes);
                        _allowedUntil = _localUnlockUntil.Value;
                    }
                }
                else if (kind == UnlockKind.OfflineOverride)
                {
                    Log("[Unlock] Offline override → 10h local grace");
                    _localUnlockUntil = now.Add(OfflineOverrideDuration);
                    _allowedUntil = _localUnlockUntil.Value;
                }
            }
            catch (Exception ex)
            {
                Log("[Unlock] Handler error: " + ex.Message);
            }
        }
    }
}
