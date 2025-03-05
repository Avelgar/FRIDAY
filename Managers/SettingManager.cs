using Friday.Managers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Friday
{
    public class SettingManager
    {
        private readonly string _filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\settings.json"));
        public Setting Setting { get; private set; }
        public SettingManager()
        {
            LoadSettings();
        }
        private void LoadSettings()
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
                    VoiceType = "Voice1",
                    Volume = 5
                };
                SaveSettings();
            }
        }
        public void SaveSettings()
        {
            string json = JsonConvert.SerializeObject(Setting, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }
        public void UpdateSettings(string assistantName, string password, string voiceType, int volume)
        {
            Setting.AssistantName = assistantName;
            Setting.Password = password;
            Setting.VoiceType = voiceType;
            Setting.Volume = volume;
            SaveSettings();
        }
    }
}
