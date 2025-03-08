using Friday.Managers;

namespace Friday
{
    public class CustomCommandService
    {
        private static VoiceService _voiceService;

        public static void Initialize(VoiceService voiceService)
        {
            _voiceService = voiceService;
        }

        public static async Task ExecuteCommand(Command command)
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

                    case "завершение процесса":
                        appProcessService.KillProcess(action.ActionText);
                        break;

                    case "открытие ссылки":
                        var browserService = new BrowserService();
                        browserService.OpenLink(action.ActionText);
                        break;

                    case "напечатать текст":
                        var keyboardService = new KeyboardService();
                        keyboardService.TypeText(action.ActionText);
                        break;

                    default:
                        Console.WriteLine($"Неизвестное действие: {action.ActionType}");
                        break;
                }
            }
        }
    }
}
