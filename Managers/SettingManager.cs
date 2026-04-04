using Friday.Managers;
using Friday.Services;
using Newtonsoft.Json;
using System.IO;

namespace Friday
{
    public class SettingManager
    {
        private readonly string _filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\settings.json"));
        private readonly string _defaultMusicFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        public static Setting Setting { get; private set; }

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
                EnsureSettingDefaults();
            }
            else
            {
                Setting = new Setting
                {
                    AssistantName = "пятница",
                    Password = "",
                    VoiceType = "Aleksandr",
                    Volume = 5,
                    InputMode = "Имя-ответ-команда",
                    MusicFolderPath = _defaultMusicFolderPath
                };
                SaveSettings();
            }
        }

        private void EnsureSettingDefaults()
        {
            if (Setting == null)
            {
                Setting = new Setting
                {
                    AssistantName = "пятница",
                    Password = "",
                    VoiceType = "Aleksandr",
                    Volume = 5,
                    InputMode = "Имя-ответ-команда",
                    MusicFolderPath = _defaultMusicFolderPath
                };
                SaveSettings();
                return;
            }

            bool changed = false;

            if (string.IsNullOrWhiteSpace(Setting.AssistantName))
            {
                Setting.AssistantName = "пятница";
                changed = true;
            }

            if (Setting.Password == null)
            {
                Setting.Password = string.Empty;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(Setting.VoiceType))
            {
                Setting.VoiceType = "Aleksandr";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(Setting.InputMode))
            {
                Setting.InputMode = "Имя-ответ-команда";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(Setting.MusicFolderPath))
            {
                Setting.MusicFolderPath = _defaultMusicFolderPath;
                changed = true;
            }

            if (changed)
            {
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            string json = JsonConvert.SerializeObject(Setting, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }

        public void UpdateSettings(string assistantName, string password, string voiceType, int volume, string inputMode, string musicFolderPath)
        {
            Setting.AssistantName = assistantName;
            Setting.Password = password;
            Setting.VoiceType = voiceType;
            Setting.Volume = volume;
            Setting.InputMode = inputMode;
            Setting.MusicFolderPath = musicFolderPath;
            SaveSettings();

            OnSettingsChanged(new SettingChangedEventArgs
            {
                AssistantName = assistantName,
                VoiceType = voiceType
            });

            AppServices.UpdateVariables();
        }

        public virtual void OnSettingsChanged(SettingChangedEventArgs e)
        {
            SettingsChanged?.Invoke(this, e);
        }
    }

    public class SettingChangedEventArgs : EventArgs
    {
        public string AssistantName { get; set; }
        public string VoiceType { get; set; }
    }
}