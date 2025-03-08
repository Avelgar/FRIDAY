using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace Friday
{
    public class AppProcessService
    {
        private bool secureSystemProcesses = true;

        public bool KillProcess(string processName)
        {
            bool isKilled = false;
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (secureSystemProcesses && IsSystemProcess(process))
                    {
                        MessageBox.Show($"Попытка завершить системный процесс: {process.ProcessName}. Операция отменена.", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }

                    process.Kill();
                    process.WaitForExit();
                    isKilled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось завершить процесс {process.ProcessName}: {ex.Message}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
                    isKilled = false;
                }
            }
            return isKilled;
        }
        public void OpenFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show($"Файл не найден: {filePath}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть файл: {ex.Message}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public bool IsProcessRunning(string processName)
        {
            return Process.GetProcessesByName(processName).Any();
        }

        private bool IsSystemProcess(Process process)
        {
            string[] systemProcesses = { "explorer", "System", "svchost", "lsass", "winlogon" };
            return systemProcesses.Contains(process.ProcessName.ToLower());
        }

    }
}
