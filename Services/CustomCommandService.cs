using Friday.Managers;

namespace Friday
{
    public class CustomCommandService
    {
        private static VoiceService _voiceService;
        private static int _passwordAttempts = 2;
        private static TaskCompletionSource<string> _passwordTaskCompletionSource;

        public static void Initialize(VoiceService voiceService)
        {
            _voiceService = voiceService;
            _voiceService.OnPasswordReceived += HandlePasswordReceived;
        }
        private static void HandlePasswordReceived(string password)
        {
            _passwordTaskCompletionSource?.TrySetResult(password);
        }
        public static async Task ExecuteCommand(Command command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command), "Команда не может быть null.");

            if (command.IsPassword)
            {
                bool isPasswordCorrect = await CheckPasswordAsync();
                if (!isPasswordCorrect)
                {
                    await _voiceService.SpeakAsync("Бот", "Попытки ввода пароля исчерпаны. Повторите вызов ассистента.");
                    return;
                }
            }

            var appProcessService = new AppProcessService();
            bool hasVoiceResponse = command.Actions.Any(a => a.ActionType.Equals("голосовой ответ", StringComparison.OrdinalIgnoreCase));

            if (!hasVoiceResponse)
            {
                await _voiceService.SpeakAsync("Бот", "Выполняю");
            }

            foreach (var action in command.Actions)
            {
                string actionType = action.ActionType.ToLower();
                string text = action.ActionText ?? "";

                switch (actionType)
                {
                    // === Локальные / Простые системные команды ===
                    case "открытие файла":
                        appProcessService.OpenFile(text);
                        break;
                    case "открыть папку":
                        appProcessService.OpenFolder(text);
                        break;
                    case "завершение процесса":
                        appProcessService.KillProcess(text);
                        break;
                    case "открытие ссылки":
                        new BrowserService().OpenLink(text);
                        break;
                    case "напечатать текст":
                        new KeyboardService().TypeText(text);
                        break;
                    case "отправить уведомление":
                        new NotificationService().SendNotification(text);
                        break;
                    case "нажать кнопку мыши":
                        new MouseService().PressMouseButton(text);
                        break;
                    case "переместить мышь":
                        new MouseService().MoveMouse(text);
                        break;

                    // === НОВОЕ: Ожидание (в секундах) ===
                    case "ожидание":
                        if (int.TryParse(text, out int seconds))
                        {
                            await Task.Delay(seconds * 1000);
                        }
                        break;

                    case "голосовой ответ":
                    case "текстовой ответ":
                    case "очистка истории":
                    case "изменение громкости":
                    case "изменение яркости":
                    case "режим камеры":
                    case "выключить режим камеры":
                    case "скриншот":
                    case "музыка":
                    case "погода":
                    case "смена имени":
                    case "смена голоса":
                        var voiceAction = new VoiceService.Actions
                        {
                            ActionType = actionType,
                            ActionText = text,
                            Sender = "Бот",
                            IsLocal = true
                        };
                        await _voiceService.ProcessAction(voiceAction);
                        break;

                    default:
                        Console.WriteLine($"Неизвестное действие: {actionType}");
                        break;
                }
            }
        }

        private static async Task<bool> CheckPasswordAsync()
        {
            SettingManager settingManager = new SettingManager();
            string correctPassword = SettingManager.Setting.Password;

            if (string.IsNullOrEmpty(correctPassword))
            {
                return true;
            }

            for (int i = 0; i < _passwordAttempts; i++)
            {
                await _voiceService.SpeakAsync("Бот", "Введите пароль:");
                string recognizedPassword = await RecognizePasswordAsync();

                if (string.IsNullOrEmpty(recognizedPassword))
                {
                    await _voiceService.SpeakAsync("Бот", "Пароль не распознан. Повторите попытку.");
                    continue;
                }

                if (recognizedPassword.Equals(correctPassword, StringComparison.OrdinalIgnoreCase))
                {
                    _voiceService.ListeningState.IsListeningForPassword = false;
                    return true;
                }
                else
                {
                    await _voiceService.SpeakAsync("Бот", "Неверный пароль. Повторите попытку.");
                }
            }

            _voiceService.ListeningState.IsListeningForPassword = false;
            return false;
        }

        private static async Task<string> RecognizePasswordAsync()
        {
            try
            {
                _voiceService.ListeningState.IsListeningForPassword = true;

                _passwordTaskCompletionSource = new TaskCompletionSource<string>();

                string recognizedText = await _passwordTaskCompletionSource.Task;

                _voiceService.ListeningState.IsListeningForPassword = false;

                return recognizedText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при распознавании пароля: {ex.Message}");
                return null;
            }
        }
    }
}
