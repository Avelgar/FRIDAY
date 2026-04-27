// RenameService.cs

namespace Friday
{
    public class RenameService
    {
        private string _botName;
        private readonly SettingManager _settingManager;

        public string BotName
        {
            // Геттер теперь сначала синхронизирует имя из настроек,
            // а потом возвращает его.
            get
            {
                // Если имя в настройках изменилось, обновляем нашу "копию"
                if (_botName != SettingManager.Setting.AssistantName)
                {
                    _botName = SettingManager.Setting.AssistantName;
                }
                return _botName;
            }

            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _botName = value; // Обновляем свою "копию"

                    // Обновляем настройки и сохраняем
                    SettingManager.Setting.AssistantName = value;
                    _settingManager.SaveSettings();

                    // Оповещаем UI
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