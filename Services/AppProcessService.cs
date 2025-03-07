using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace Friday
{
    public class AppProcessService
    {
        private bool secureSystemProcesses = true; // Защита от завершения системных процессов

        public bool KillProcess(string processName)
        {
            bool isKilled = false;
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    // Проверка на защищенные системные процессы
                    if (secureSystemProcesses && IsSystemProcess(process))
                    {
                        Console.WriteLine($"Попытка завершить системный процесс: {process.ProcessName}. Операция отменена.");
                        continue; // Пропускаем системные процессы
                    }

                    process.Kill();
                    process.WaitForExit(); // Ждем завершения процесса
                    isKilled = true;
                    Console.WriteLine($"Процесс {process.ProcessName} завершен.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Не удалось завершить процесс {process.ProcessName}: {ex.Message}");
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
                    // Создаем новый процесс для открытия файла
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true // Используем оболочку для открытия файла
                    });
                }
                else
                {
                    MessageBox.Show($"Файл не найден: {filePath}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                // Обработка ошибок, если файл не может быть открыт
                MessageBox.Show($"Не удалось открыть файл: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public bool IsProcessRunning(string processName)
        {
            return Process.GetProcessesByName(processName).Any();
        }

        private bool IsSystemProcess(Process process)
        {
            // Здесь можно добавить логику для определения системных процессов
            // Например, проверка по имени или ID процесса
            // Для простоты, можно оставить только проверку по имени
            string[] systemProcesses = { "explorer", "System", "svchost", "lsass", "winlogon" }; // Примеры системных процессов
            return systemProcesses.Contains(process.ProcessName.ToLower());
        }

    }
}