using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgentClient
{
    internal static class Program
    {
        // Replace with your server URL if different
        private static readonly Uri ServerEndpoint =
            new Uri("https://pc-admin-server.onrender.com/api/agent/status");

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

        private static DateTimeOffset _allowedUntil = DateTimeOffset.MaxValue;
        private static DateTimeOffset _lastContactOk = DateTimeOffset.MinValue;
        private static DateTimeOffset? _lockedAt = null;

        private static async Task Main(string[] args)
        {
            Console.Title = "AgentClient";
            Console.WriteLine("[Agent] Starting...");

            // Manual test hotkeys
            _ = Task.Run(async () =>
            {
                Console.WriteLine("[Agent] Press 'L' to LOCK, 'U' to UNLOCK");
                while (true)
                {
                    if (Console.KeyAvailable)
                    {
                        var k = Console.ReadKey(true).Key;
                        if (k == ConsoleKey.L) LockScreen.Show("admin123");
                        if (k == ConsoleKey.U) LockScreen.Hide();
                    }
                    await Task.Delay(50);
                }
            });

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
                Console.WriteLine("[Agent] Status: " + json);

                bool sent = false;
                bool requireLock = false;

                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var resp = await Http.PostAsync(ServerEndpoint, content);
                    var body = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Agent] Server: {(int)resp.StatusCode} {body}");

                    if (resp.IsSuccessStatusCode)
                    {
                        var doc = JsonDocument.Parse(body);
                        var policy = doc.RootElement.GetProperty("policy");
                        var auStr = policy.GetProperty("allowedUntil").GetString();
                        requireLock = policy.GetProperty("requireLock").GetBoolean();

                        if (DateTimeOffset.TryParse(auStr, out var au))
                            _allowedUntil = au;

                        _lastContactOk = now;
                        sent = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Agent] Send error: " + ex.Message);
                }

                if (!sent)
                {
                    var offlineFor = now - _lastContactOk;
                    Console.WriteLine($"[Agent] Offline approx: {offlineFor.TotalSeconds:F0}s");
                }

                // Auto-lock by policy
                bool shouldLock = requireLock || now >= _allowedUntil;

                if (shouldLock && !LockScreen.IsShown)
                {
                    Console.WriteLine("[Agent] Policy -> SHOW lock screen");
                    LockScreen.Show("admin123", "Policy: access time finished");
                    _lockedAt = now;
                }
                else if (!shouldLock && LockScreen.IsShown)
                {
                    Console.WriteLine("[Agent] Policy -> HIDE lock screen");
                    LockScreen.Hide();
                    _lockedAt = null;
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}
