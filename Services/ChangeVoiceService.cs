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
            SettingManager.Setting.VoiceType = voice;
            _settingManager.SaveSettings();

            _settingManager.OnSettingsChanged(new SettingChangedEventArgs
            {
                VoiceType = voice
            });
        }
    }
}