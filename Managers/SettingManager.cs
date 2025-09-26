using Friday.Managers;
using Newtonsoft.Json;
using System.IO;
using System;

namespace Friday
{
    public class SettingManager
    {
        private readonly string _filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\settings.json"));
        public Setting Setting { get; private set; }

        // Событие для уведомления об изменениях настроек
        public event EventHandler<SettingChangedEventArgs> SettingsChanged;

        public SettingManager()
        {
            LoadSettings();
        }

        public void LoadSettings()
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                Setting = JsonConvert.DeserializeObject<Setting>(json);
            }
            else
            {
                Setting = new Setting
                {
                    AssistantName = "пятница",
                    Password = "",
                    VoiceType = "Aleksandr",
                    Volume = 5,
                    InputMode = "Имя-ответ-команда"
                };
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            string json = JsonConvert.SerializeObject(Setting, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }

        public void UpdateSettings(string assistantName, string password, string voiceType, int volume, string inputMode)
        {
            Setting.AssistantName = assistantName;
            Setting.Password = password;
            Setting.VoiceType = voiceType;
            Setting.Volume = volume;
            Setting.InputMode = inputMode;
            SaveSettings();

            // Уведомляем подписчиков об изменениях
            OnSettingsChanged(new SettingChangedEventArgs
            {
                AssistantName = assistantName,
                VoiceType = voiceType
            });
        }

        // Метод для вызова события
        public virtual void OnSettingsChanged(SettingChangedEventArgs e)
        {
            SettingsChanged?.Invoke(this, e);
        }
    }

    // Класс для передачи данных о измененных настройках
    public class SettingChangedEventArgs : EventArgs
    {
        public string AssistantName { get; set; }
        public string VoiceType { get; set; }
    }
}