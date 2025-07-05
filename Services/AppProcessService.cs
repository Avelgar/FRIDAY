using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System;
using System.Runtime.InteropServices;

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
                        System.Windows.MessageBox.Show($"Попытка завершить системный процесс: {process.ProcessName}. Операция отменена.", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }

                    process.Kill();
                    process.WaitForExit();
                    isKilled = true;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Не удалось завершить процесс {process.ProcessName}: {ex.Message}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    System.Windows.MessageBox.Show($"Файл не найден: {filePath}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось открыть файл: {ex.Message}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenFolder(string folderPath)
        {
            try
            {
                if (Directory.Exists(folderPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = folderPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    System.Windows.MessageBox.Show($"Папка не найдена: {folderPath}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось открыть папку: {ex.Message}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        public void SetVolume(string volumeStr)
        {
            if (!int.TryParse(volumeStr, out int volume) || volume < 0 || volume > 100)
            {
                System.Windows.MessageBox.Show("Громкость должна быть в диапазоне от 0 до 100.", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            float volumeScalar = volume / 100f; // Преобразуем в диапазон от 0.0 до 1.0

            var deviceEnumerator = new MMDeviceEnumerator();
            var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            if (device != null)
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = volumeScalar;
            }
            else
            {
                System.Windows.MessageBox.Show("Не удалось получить устройство воспроизведения.", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SetBrightness(string brightnessStr)
        {
            if (!int.TryParse(brightnessStr, out int brightness) || brightness < 0 || brightness > 100)
            {
                MessageBox.Show("Яркость должна быть в диапазоне от 0 до 100.", "Ошибка!",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"(Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightnessMethods).WmiSetBrightness(1,{brightness})",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                Process.Start(psi)?.WaitForExit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось изменить яркость: {ex.Message}", "Ошибка!",
                               MessageBoxButton.OK, MessageBoxImage.Error);
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
