using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace Friday
{
    public class AppProcessService
    {
        private bool secureSystemProcesses = true; //мб оно и так не будет их завершать
        public bool KillProcess(string processName)
        {
            bool isKilled = false;
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill();
                    isKilled = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    isKilled = false;
                }
            }
            return isKilled;
        }
        public void OpenFile(string filePath)
        {
            try
            {
                // Проверяем, существует ли файл
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
    }
}
