using System;
using System.Diagnostics;

namespace Friday.Services
{
    public class BrightnessService
    {
        public void SetBrightness(string brightnessStr)
        {
            if (!int.TryParse(brightnessStr, out int brightness) || brightness < 0 || brightness > 100) return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"(Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightnessMethods).WmiSetBrightness(1,{brightness})",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                Process.Start(psi)?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SetBrightness Error]: {ex.Message}");
            }
        }
    }
}
