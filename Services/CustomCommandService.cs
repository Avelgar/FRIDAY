using Friday.Managers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Friday
{
    public class CustomCommandService
    {
        private static VoiceService _voiceService; // Поле для VoiceService

        public static void Initialize(VoiceService voiceService)
        {
            _voiceService = voiceService; // Инициализируем VoiceService
        }

        public static async Task ExecuteCommand(Command command) // Изменяем на Task вместо void
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command), "Команда не может быть null.");
            }

            var appProcessService = new AppProcessService();
            bool hasVoiceResponse = false;

            foreach (var action in command.Actions)
            {
                if (action.ActionType.Equals("голосовой ответ", StringComparison.OrdinalIgnoreCase))
                {
                    hasVoiceResponse = true;
                    await _voiceService.SpeakAsync(action.ActionText);
                }
            }

            if (!hasVoiceResponse)
            {
                await _voiceService.SpeakAsync("Выполняю");
            }

            foreach (var action in command.Actions)
            {
                switch (action.ActionType.ToLower())
                {
                    case "открытие файла":
                        appProcessService.OpenFile(action.ActionText);
                        break;


                    default:
                        Console.WriteLine($"Неизвестное действие: {action.ActionType}");
                        break;
                }
            }
        }
    }
}
