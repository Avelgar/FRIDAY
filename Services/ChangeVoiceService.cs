using System;

namespace Friday.Services
{
    public class ChangeVoiceService
    {
        private readonly SettingManager _settingManager;

        public ChangeVoiceService(SettingManager settingManager)
        {
            _settingManager = settingManager;
        }

        public void ChangeVoice(string voice)
        {
            _settingManager.Setting.VoiceType = voice;
            _settingManager.SaveSettings();

            // Уведомляем об изменении голоса
            _settingManager.OnSettingsChanged(new SettingChangedEventArgs
            {
                VoiceType = voice
            });
        }
    }
}