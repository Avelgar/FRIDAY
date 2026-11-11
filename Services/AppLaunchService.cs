using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Friday.Services
{
    public class AppLaunchService
    {
        private static readonly Dictionary<string, string[]> LaunchAliases = new(StringComparer.OrdinalIgnoreCase)
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
            { "павершелл", new[] { "powershell" } },
            { "powershell", new[] { "powershell" } },
            { "телеграм", new[] { "telegram", "telegram desktop" } },
            { "телеграмм", new[] { "telegram", "telegram desktop" } },
            { "тг", new[] { "telegram", "telegram desktop" } },
            { "telegram", new[] { "telegram", "telegram desktop" } },
            { "дискорд", new[] { "discord" } },
            { "дс", new[] { "discord" } },
            { "discord", new[] { "discord" } },
            { "хром", new[] { "chrome", "google chrome" } },
            { "гугл хром", new[] { "chrome", "google chrome" } },
            { "chrome", new[] { "chrome", "google chrome" } },
            { "яндекс", new[] { "browser", "yandex", "yandex browser" } },
            { "яндекс браузер", new[] { "browser", "yandex", "yandex browser" } },
            { "yandex", new[] { "browser", "yandex", "yandex browser" } },
            { "стим", new[] { "steam", "steamwebhelper" } },
            { "steam", new[] { "steam", "steamwebhelper" } },
            { "ворд", new[] { "winword", "word" } },
            { "word", new[] { "winword", "word" } },
            { "эксель", new[] { "excel" } },
            { "excel", new[] { "excel" } },
            { "паверпоинт", new[] { "powerpnt", "powerpoint" } },
            { "powerpoint", new[] { "powerpnt", "powerpoint" } },
            { "спотифай", new[] { "spotify" } },
            { "spotify", new[] { "spotify" } },
            { "вк", new[] { "vk" } },
            { "vk", new[] { "vk" } },
            { "вскод", new[] { "code", "vscode" } },
            { "код", new[] { "code", "vscode" } },
            { "vscode", new[] { "code", "visual studio code" } },
            { "пейнт", new[] { "mspaint", "paint" } },
            { "паинт", new[] { "mspaint", "paint" } },
            { "paint", new[] { "mspaint", "paint" } },
            { "параметры", new[] { "ms-settings:" } },
            { "настройки", new[] { "ms-settings:" } },
            { "settings", new[] { "ms-settings:" } },
            { "браузер", new[] { "chrome", "browser", "msedge", "firefox", "yandex" } }
        };

        public void OpenFile(string appOrPath) => OpenApp(appOrPath);

        public void OpenApp(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            target = target.Trim().Trim('\'', '"');

            try
            {
                // 1. Прямой путь к файлу или папке
                if (File.Exists(target) || Directory.Exists(target))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        UseShellExecute = true,
                        WorkingDirectory = File.Exists(target) ? Path.GetDirectoryName(target) : target
                    });
                    return;
                }

                // 2. URL или протокол (например steam://, https://, ms-settings:, calc:)
                if (target.Contains("://") || target.StartsWith("calc:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                    return;
                }

                // 3. Быстрые системные алиасы
                string cleanTarget = target.ToLower();
                if (cleanTarget is "калькулятор" or "calc" or "calculator")
                {
                    Process.Start(new ProcessStartInfo { FileName = "calc.exe", UseShellExecute = true });
                    return;
                }
                if (cleanTarget is "блокнот" or "notepad")
                {
                    Process.Start(new ProcessStartInfo { FileName = "notepad.exe", UseShellExecute = true });
                    return;
                }
                if (cleanTarget is "проводник" or "explorer")
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true });
                    return;
                }
                if (cleanTarget is "диспетчер задач" or "taskmgr")
                {
                    Process.Start(new ProcessStartInfo { FileName = "taskmgr.exe", UseShellExecute = true });
                    return;
                }
                if (cleanTarget is "командная строка" or "cmd")
                {
                    Process.Start(new ProcessStartInfo { FileName = "cmd.exe", UseShellExecute = true });
                    return;
                }
                if (cleanTarget is "стим" or "steam")
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = "steam://open/main", UseShellExecute = true });
                        return;
                    }
                    catch { }
                }
                if (cleanTarget is "параметры" or "настройки" or "settings")
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = "ms-settings:", UseShellExecute = true });
                        return;
                    }
                    catch { }
                }

                var candidates = GetCandidates(target);

                // 4. Поиск среди ярлыков в меню Пуск и на Рабочем столе
                foreach (var candidate in candidates)
                {
                    string? shortcutPath = FindShortcut(candidate);
                    if (!string.IsNullOrEmpty(shortcutPath) && File.Exists(shortcutPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = shortcutPath,
                            UseShellExecute = true
                        });
                        return;
                    }
                }

                // 5. Поиск в реестре App Paths
                foreach (var candidate in candidates)
                {
                    string? regPath = FindInRegistryAppPaths(candidate);
                    if (!string.IsNullOrEmpty(regPath) && File.Exists(regPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = regPath,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(regPath)
                        });
                        return;
                    }
                }

                // 6. Прямой запуск через Windows Shell
                foreach (var candidate in candidates)
                {
                    try
                    {
                        string execTarget = candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? candidate : candidate + ".exe";
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = execTarget,
                            UseShellExecute = true
                        });
                        return;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenApp Error]: Не удалось открыть '{target}': {ex.Message}");
            }
        }

        private static List<string> GetCandidates(string target)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string clean = target.Trim().Trim('\'', '"');
            if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean = clean[..^4];

            if (!string.IsNullOrEmpty(clean)) candidates.Add(clean);

            if (LaunchAliases.TryGetValue(clean, out var aliases))
            {
                foreach (var a in aliases) candidates.Add(a);
            }

            foreach (var kvp in LaunchAliases)
            {
                if (clean.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (var a in kvp.Value) candidates.Add(a);
                }
            }

            return candidates.ToList();
        }

        private static string? FindInRegistryAppPaths(string appName)
        {
            string exeName = appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? appName : appName + ".exe";
            string[] subKeys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + exeName
            };

            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var subKey in subKeys)
                {
                    try
                    {
                        using var key = root.OpenSubKey(subKey);
                        if (key != null)
                        {
                            var val = key.GetValue(null)?.ToString();
                            if (!string.IsNullOrEmpty(val))
                            {
                                val = val.Trim('"', ' ');
                                if (File.Exists(val)) return val;
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private static string? FindShortcut(string appName)
        {
            var searchFolders = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            }.Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)).Distinct().ToList();

            string cleanName = appName.Trim().ToLower();
            if (string.IsNullOrEmpty(cleanName)) return null;

            var allShortcuts = new List<string>();
            foreach (var folder in searchFolders)
            {
                try
                {
                    allShortcuts.AddRange(Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories));
                }
                catch { }
            }

            var exact = allShortcuts.FirstOrDefault(f => 
                Path.GetFileNameWithoutExtension(f).Trim().Equals(cleanName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            string[] ignoreWords = { "uninstall", "деинсталл", "удалить", "remove", "setup", "help", "справка", "readme", "website", "сайт" };
            var candidateShortcuts = allShortcuts.Where(f =>
            {
                string name = Path.GetFileNameWithoutExtension(f).ToLower();
                return !ignoreWords.Any(w => name.Contains(w));
            }).ToList();

            var startsWith = candidateShortcuts.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Trim().StartsWith(cleanName, StringComparison.OrdinalIgnoreCase));
            if (startsWith != null) return startsWith;

            var partial = candidateShortcuts.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (partial != null) return partial;

            return null;
        }
    }
}
