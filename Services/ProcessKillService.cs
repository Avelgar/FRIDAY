using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Friday.Services
{
    public class ProcessKillService
    {
        private readonly bool _secureSystemProcesses = true;

        private static readonly string[] SystemProcesses = 
        { 
            "explorer", "system", "svchost", "lsass", "winlogon", "csrss", 
            "smss", "services", "spoolsv", "dwm", "taskhostw", "friday" 
        };

        private static readonly Dictionary<string, string[]> KillAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "калькулятор", new[] { "calculator", "calculatorapp", "calc" } },
            { "calc", new[] { "calculator", "calculatorapp", "calc" } },
            { "calculator", new[] { "calculator", "calculatorapp", "calc" } },
            { "блокнот", new[] { "notepad" } },
            { "notepad", new[] { "notepad" } },
            { "проводник", new[] { "explorer" } },
            { "explorer", new[] { "explorer" } },
            { "диспетчер задач", new[] { "taskmgr" } },
            { "taskmgr", new[] { "taskmgr" } },
            { "командная строка", new[] { "cmd" } },
            { "cmd", new[] { "cmd" } },
            { "павершел", new[] { "powershell" } },
            { "powershell", new[] { "powershell" } },
            { "телеграм", new[] { "telegram" } },
            { "телеграмм", new[] { "telegram" } },
            { "тг", new[] { "telegram" } },
            { "telegram", new[] { "telegram" } },
            { "дискорд", new[] { "discord" } },
            { "дс", new[] { "discord" } },
            { "discord", new[] { "discord" } },
            { "хром", new[] { "chrome" } },
            { "гугл хром", new[] { "chrome" } },
            { "chrome", new[] { "chrome" } },
            { "яндекс", new[] { "browser", "yandex" } },
            { "яндекс браузер", new[] { "browser", "yandex" } },
            { "yandex", new[] { "browser", "yandex" } },
            { "стим", new[] { "steam", "steamwebhelper" } },
            { "steam", new[] { "steam", "steamwebhelper" } },
            { "ворд", new[] { "winword" } },
            { "word", new[] { "winword" } },
            { "эксель", new[] { "excel" } },
            { "excel", new[] { "excel" } },
            { "паверпоинт", new[] { "powerpnt" } },
            { "powerpoint", new[] { "powerpnt" } },
            { "спотифай", new[] { "spotify" } },
            { "spotify", new[] { "spotify" } },
            { "вк", new[] { "vk" } },
            { "vk", new[] { "vk" } },
            { "вскод", new[] { "code" } },
            { "код", new[] { "code" } },
            { "vscode", new[] { "code" } },
            { "пейнт", new[] { "mspaint" } },
            { "паинт", new[] { "mspaint" } },
            { "paint", new[] { "mspaint" } },
            { "браузер", new[] { "chrome", "browser", "msedge", "firefox" } }
        };

        public bool KillProcess(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;

            var searchNames = new HashSet<string>(GetCandidates(target), StringComparer.OrdinalIgnoreCase);
            bool anyKilled = false;

            try
            {
                var allProcesses = Process.GetProcesses();
                foreach (var process in allProcesses)
                {
                    try
                    {
                        string pName = process.ProcessName;
                        string wTitle = process.MainWindowTitle;

                        bool match = searchNames.Any(name => 
                            pName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                            pName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (!string.IsNullOrEmpty(wTitle) && wTitle.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0));

                        if (!match) continue;

                        if (_secureSystemProcesses && IsSystemProcess(process))
                            continue;

                        // Сначала вежливо пробуем закрыть
                        if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                        {
                            process.CloseMainWindow();
                            if (!process.WaitForExit(700))
                            {
                                process.Kill();
                            }
                        }
                        else if (!process.HasExited)
                        {
                            process.Kill();
                        }

                        anyKilled = true;
                    }
                    catch { }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KillProcess Error]: {ex.Message}");
            }

            return anyKilled;
        }

        private static List<string> GetCandidates(string target)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string clean = target.Trim().Trim('\'', '"');
            if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean = clean[..^4];

            if (!string.IsNullOrEmpty(clean)) candidates.Add(clean);

            if (KillAliases.TryGetValue(clean, out var aliases))
            {
                foreach (var a in aliases) candidates.Add(a);
            }

            foreach (var kvp in KillAliases)
            {
                if (clean.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (var a in kvp.Value) candidates.Add(a);
                }
            }

            return candidates.ToList();
        }

        private bool IsSystemProcess(Process process)
        {
            try
            {
                string name = process.ProcessName.ToLower();
                return SystemProcesses.Contains(name);
            }
            catch
            {
                return true;
            }
        }
    }
}
