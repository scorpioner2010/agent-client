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
        private static readonly Uri ServerEndpoint =
            new Uri("https://pc-admin-server.onrender.com/api/agent/status");

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

        // Policy state
        private static DateTimeOffset _allowedUntil = DateTimeOffset.MaxValue;
        private static DateTimeOffset _lastContactOk = DateTimeOffset.MinValue;

        // Server-driven manual unlock grace (minutes)
        private static int _manualUnlockGraceMinutes = 10;

        // Local override applied after manual unlock while OFFLINE
        private static DateTimeOffset? _localUnlockUntil = null;

        private const string AdminPassword = "admin123"; // TODO: move to secure storage
        private const string LogFile = "agent.log";
        private static readonly object LogLock = new();

        private static void Log(string msg)
        {
            try
            {
                lock (LogLock)
                {
                    File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\r\n");
                }
            }
            catch { /* ignore */ }
        }

        private static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { File.WriteAllText("agent_crash.log", e.ExceptionObject?.ToString() ?? "null"); } catch {}
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try { File.AppendAllText("agent_crash.log", "\n" + e.Exception); } catch {}
                e.SetObserved();
            };

            Log("Agent starting...");

            // Subscribe to manual-unlock event: start local offline grace
            LockScreen.OnUnlocked += () =>
            {
                var now = DateTimeOffset.Now;
                if (_manualUnlockGraceMinutes < 0) _manualUnlockGraceMinutes = 0;
                _localUnlockUntil = now.AddMinutes(_manualUnlockGraceMinutes);
                _allowedUntil = _localUnlockUntil.Value; // prevent immediate re-lock
                Log($"[Local] Manual unlock, grace until {_localUnlockUntil:O} (minutes={_manualUnlockGraceMinutes})");
            };

            string machineName = Environment.MachineName;
            string os = RuntimeInformation.OSDescription;

            while (true)
            {
                var now = DateTimeOffset.Now;
                var uptimeSeconds = Environment.TickCount64 / 1000;

                var status = new
                {
                    Machine = machineName,
                    OS = os,
                    UptimeSec = uptimeSeconds,
                    Time = now.ToString("O")
                };

                string json = JsonSerializer.Serialize(status);
                Log("[Status] " + json);

                bool sent = false;
                bool requireLock = false;

                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var resp = await Http.PostAsync(ServerEndpoint, content);
                    var body = await resp.Content.ReadAsStringAsync();
                    Log($"[Server] {(int)resp.StatusCode} {body}");

                    if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body))
                    {
                        using var doc = JsonDocument.Parse(body);
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
                                if (v < 0) v = 0;
                                if (v > 240) v = 240;
                                _manualUnlockGraceMinutes = v;
                            }
                        }

                        _lastContactOk = now;
                        sent = true;

                        // When online again, local manual override no longer applies
                        _localUnlockUntil = null;
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

                bool shouldLock;
                if (!sent) // OFFLINE: allow local manual-unlock grace to override
                {
                    shouldLock = !localOverrideActive && now >= _allowedUntil;
                }
                else // ONLINE: server policy dominates
                {
                    shouldLock = requireLock || now >= _allowedUntil;
                }

                if (shouldLock && !LockScreen.IsShown)
                {
                    Log("[Policy] SHOW lock screen");
                    LockScreen.Show(AdminPassword, "Policy: access time finished");
                }
                else if (!shouldLock && LockScreen.IsShown)
                {
                    Log("[Policy] HIDE lock screen");
                    LockScreen.Hide();
                }
                // -----------------------

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }
}
