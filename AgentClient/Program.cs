using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgentClient
{
    internal static class Program
    {
        // Якщо зміниш домен/порт — підстав свій
        private static readonly Uri ServerEndpoint = new Uri("https://pc-admin-server.onrender.com/api/agent/status");
        private static readonly HttpClient Http = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };

        private static DateTimeOffset _allowedUntil = DateTimeOffset.MaxValue;   // останній дедлайн із сервера
        private static DateTimeOffset _lastContactOk = DateTimeOffset.MinValue;  // коли востаннє успішно дістався сервера
        private static DateTimeOffset? _lockedAt = null;                          // коли показали лок-екран

        private static async Task Main(string[] args)
        {
            Console.Title = "AgentClient";
            Console.WriteLine("[Agent] Starting...");

            // Тест-клавіші: L — LOCK, U — UNLOCK
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
                        {
                            _allowedUntil = au;
                        }

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

                // --- АВТО-ЛОК ЗА ПОЛІТИКОЮ ---
                bool shouldLock = requireLock || now >= _allowedUntil;

                if (shouldLock && !LockScreen.IsShown)
                {
                    Console.WriteLine("[Agent] Policy -> SHOW lock screen");
                    LockScreen.Show("admin123", "Політика: доступ завершено");
                    _lockedAt = now;
                }
                else if (!shouldLock && LockScreen.IsShown)
                {
                    Console.WriteLine("[Agent] Policy -> HIDE lock screen");
                    LockScreen.Hide();
                    _lockedAt = null;
                }
                // --- кінець автолоку ---

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }
}
