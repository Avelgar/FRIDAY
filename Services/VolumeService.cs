using NAudio.CoreAudioApi;
using System;
using System.Diagnostics;

namespace Friday.Services
{
    public class VolumeService
    {
        public void SetVolume(string volumeStr)
        {
            if (!int.TryParse(volumeStr, out int volume) || volume < 0 || volume > 100) return;

            try
            {
                float volumeScalar = volume / 100f;
                using var deviceEnumerator = new MMDeviceEnumerator();
                using var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (device != null)
                {
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = volumeScalar;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SetVolume Error]: {ex.Message}");
            }
        }
    }
}
