using Friday.Managers;

namespace Friday
{
    public class RenameService
    {
        private string _botName;
        private readonly SettingManager _settingManager;

        public string BotName
        {
            get => _botName;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _botName = value;
                    _settingManager.Setting.AssistantName = value;
                    _settingManager.SaveSettings();

                    // Уведомляем об изменении имени
                    _settingManager.OnSettingsChanged(new SettingChangedEventArgs
                    {
                        AssistantName = value
                    });
                }
            }
        }

        public RenameService(string initialName, SettingManager settingManager)
        {
            _botName = initialName;
            _settingManager = settingManager;
        }
    }
}