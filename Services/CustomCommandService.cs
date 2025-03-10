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
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command), "Команда не может быть null.");
            }

            if (command.IsPassword)
            {
                bool isPasswordCorrect = await CheckPasswordAsync();
                if (!isPasswordCorrect)
                {
                    await _voiceService.SpeakAsync("Попытки ввода пароля исчерпаны. Повторите вызов ассистента.");
                    return;
                }
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

                    case "отправить уведомление":
                        var notificationService = new NotificationService();
                        notificationService.SendNotification(action.ActionText);
                        break;

                    default:
                        Console.WriteLine($"Неизвестное действие: {action.ActionType}");
                        break;
                }
            }
        }

        private static async Task<bool> CheckPasswordAsync()
        {
            SettingManager settingManager = new SettingManager();
            string correctPassword = settingManager.Setting.Password;

            if (string.IsNullOrEmpty(correctPassword))
            {
                return true;
            }

            for (int i = 0; i < _passwordAttempts; i++)
            {
                await _voiceService.SpeakAsync("Введите пароль:");
                string recognizedPassword = await RecognizePasswordAsync();

                if (string.IsNullOrEmpty(recognizedPassword))
                {
                    await _voiceService.SpeakAsync("Пароль не распознан. Повторите попытку.");
                    continue;
                }

                if (recognizedPassword.Equals(correctPassword, StringComparison.OrdinalIgnoreCase))
                {
                    _voiceService.ListeningState.IsListeningForPassword = false;
                    return true;
                }
                else
                {
                    await _voiceService.SpeakAsync("Неверный пароль. Повторите попытку.");
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
