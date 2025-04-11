using System.Text;
using NAudio.Wave;
using Vosk;
using Newtonsoft.Json;
using System.Net.Http;
using System.IO;
using static Friday.VoiceService;
using System.Windows;
using Newtonsoft.Json.Serialization;
using System;
using Friday.Managers;
using static System.Net.Mime.MediaTypeNames;

namespace Friday
{

    public interface IVoiceService
    {
        Task ProcessCommand(string command); // Убедитесь, что возвращаемый тип - Task
        Task OnMessageReceived(string message); // Добавьте метод, если он нужен
        Task SpeakAsync(string text); // Добавьте метод, если он нужен
    }
    public class VoiceService
    {
        private string modelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\model"));
        public event Action<string> OnPasswordReceived;
        private readonly VoskRecognizer _recognizer;
        private readonly RenameService _renameService;
        private readonly SettingManager _settingManager;
        private WaveInEvent _waveIn;
        private static MusicService musicService = new MusicService();
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly string apiToken = "c83ecc77-ec9d-4055-b2ae-a16b09991421";
        private static readonly string synthesisUrl = $"https://public.api.voice.steos.io/api/v1/synthesize-controller/synthesis-by-text?authToken={apiToken}";
        public ListeningState ListeningState { get; private set; }

        public event Action<string> OnMessageReceived;

        private List<string> _installedApplications;

        private StringBuilder dialogueHistory = new StringBuilder();

        public VoiceService(RenameService renameService, SettingManager settingManager)
        {
            _renameService = renameService;
            _settingManager = settingManager;
            Vosk.Vosk.SetLogLevel(-1);
            Model model = new Model(modelPath);
            _recognizer = new VoskRecognizer(model, 16000.0f);
            _recognizer.SetMaxAlternatives(1);
            _recognizer.SetWords(true);

            ListeningState = new ListeningState();
        }
        public static RenameService CreateRenameServiceFromSettings()
        {
            SettingManager settingManager = new SettingManager();
            return new RenameService(settingManager.Setting.AssistantName);
        }

        public async Task StartListening()
        {
            try
            {
                _waveIn = new WaveInEvent();
                _waveIn.WaveFormat = new WaveFormat(16000, 1);

                if (WaveIn.DeviceCount == 0)
                {
                    OnMessageReceived?.Invoke("Нет доступных устройств для записи.");
                    return;
                }

                _waveIn.DeviceNumber = 0;
                string lastRecognizedText = string.Empty;
                bool isListeningForCommands = false;

                _waveIn.DataAvailable += async (sender, e) =>
                {
                    try
                    {
                        if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                        {
                            var result = _recognizer.Result();
                            var response = JsonConvert.DeserializeObject<RecognitionResponse>(result);
                            var recognizedText = response?.Alternatives.FirstOrDefault()?.Text;

                            if (ListeningState.IsListeningForPassword)
                            {
                                OnPasswordReceived?.Invoke(recognizedText);
                                return;
                            }

                            if (recognizedText == _renameService.BotName.ToLower())
                            {
                                OnMessageReceived?.Invoke($"Распознано: {recognizedText}");
                                await SpeakAsync("Слушаю вас");
                                ListeningState.StartListening();
                                isListeningForCommands = true;
                                lastRecognizedText = string.Empty;
                            }
                            else if (isListeningForCommands && !string.IsNullOrEmpty(recognizedText) && recognizedText != lastRecognizedText)
                            {
                                ProcessCommand(recognizedText);
                                isListeningForCommands = false;
                                lastRecognizedText = recognizedText;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OnMessageReceived?.Invoke($"Ошибка при обработке аудиоданных: {ex.Message}");
                    }
                };

                _waveIn.StartRecording();
                OnMessageReceived?.Invoke("Началась запись.");
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при запуске записи: {ex.Message}");
            }
        }

        public void StopListening()
        {
            try
            {
                if (_waveIn != null)
                {
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                    _waveIn = null;
                    OnMessageReceived?.Invoke("Запись остановлена.");
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при остановке записи: {ex.Message}");
            }
        }

        public List<string> GetInstalledApplications()
        {
            return _installedApplications; // Возвращаем сохраненный список
        }

        public void SetInstalledApplications(List<string> installedApplications)
        {
            _installedApplications = installedApplications; // Сохраняем список установленных приложений
        }

        public async Task ProcessCommand(string command)
        {
            OnMessageReceived?.Invoke($"Распознано: {command}");

            if (ListeningState.IsListeningForPassword)
            {
                OnPasswordReceived?.Invoke(command);
                return;
            }
            CommandManager commandManager = new CommandManager();
            var customCommand = commandManager.FindCommandByTrigger(command);
            if (customCommand != null)
            {
                await CustomCommandService.ExecuteCommand(customCommand);
            }
            else
            {
                var installedApps = GetInstalledApplications();
                string appsList = string.Join(", ", installedApps);
                var appProcessService = new AppProcessService();
                var processes = System.Diagnostics.Process.GetProcesses();
                var processList = processes.Select(p => $"{p.ProcessName} (ID: {p.Id})").ToList();
                string processOutput = string.Join(", ", processList);
                string response = await GeminiService.GenerateTextAsync($"Представь, что ты помощник на компьютере у человека. " +
                    $"Ты должен дать ответ ввиде тип|действие;тип|действие(если у тебя одна пара тип|действие, ТО ТОЧКУ С ЗАПЯТОЙ НЕ СТАВЬ). " +
                    $"Например голосовой ответ|привет;завершение процесса|chrome;открытие ссылки|https://www.youtube.com вот все типы и что они принимают на вход: " +
                    $"открытие файла(принимает путь до файла), завершение процесса(принимает название процесса из списка процессов), открытие ссылки(принимает ссылку), напечатать текст(принимает текст), " +
                    $"нажать кнопку мыши(принимает текст: пкм, скм или лкм), отправить уведомление(принимает текст уведомления), голосовой ответ(принимает текст), " +
                    $"музыка(принимает текст: включить музыку, выключить музыку или следующий трек), погода(принимает текст: сегодня, завтра или послезавтра), смена имени(принимает текст: новое имя). " +
                    $"Команда, которую ты выдашь будет обрабатываться через эти типы и в зависимости от типа будет просиходить какое-то действие. Каждый раз давай хотя бы один голосовой ответ! Вот пути ко всем установленным приложениям если нужно:{appsList}. " +
                    $"Вот все запущенные на данный момент процессы: {processOutput}. " +
                    $"Вот время: {DateTime.Now.ToShortTimeString()}. " +
                    $"Вот запрос пользователя:" + command + $". " +
                    $"Вот история диалога, некоторые ответы нужно строить исходя из неё:{dialogueHistory}.");
                dialogueHistory.AppendLine($"Распознано: {command}");
                dialogueHistory.AppendLine($"Ответ: {response}");
                // Создаем список действий
                List<ActionItem> actions = new List<ActionItem>();

                // Обработка ответа
                if (response.Contains(";"))
                {
                    actions = response.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(a => a.Split('|'))
                                        .Select(a => new ActionItem
                                        {
                                            ActionType = a[0].Trim(),
                                            ActionText = a[1].Trim()
                                        })
                                        .ToList();
                }
                else
                {
                    var singleAction = response.Split('|');
                    actions.Add(new ActionItem
                    {
                        ActionType = singleAction[0].Trim(),
                        ActionText = singleAction[1].Trim()
                    });
                }



                // Дальнейшая обработка действий
                foreach (var action in actions)
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

                        case "нажать кнопку мыши":
                            var mouseService = new MouseService();
                            mouseService.PressMouseButton(action.ActionText);
                            break;

                        case "голосовой ответ":
                            string[] words = action.ActionText.Split(' ');
                            StringBuilder currentPart = new StringBuilder();

                            foreach (var word in words)
                            {
                                // Проверяем, если добавление следующего слова не превышает 50 символов
                                if (currentPart.Length + word.Length + 1 <= 50) // +1 для пробела
                                {
                                    if (currentPart.Length > 0)
                                    {
                                        currentPart.Append(' ');
                                    }
                                    currentPart.Append(word);
                                }
                                else
                                {
                                    // Здесь вы можете обработать текущую часть текста
                                    await SpeakAsync(currentPart.ToString());
                                    currentPart.Clear(); // Очищаем для новой части
                                    currentPart.Append(word); // Добавляем текущее слово
                                }
                            }
                            // Обработка оставшейся части
                            if (currentPart.Length > 0)
                            {
                                await SpeakAsync(currentPart.ToString());
                            }
                            break;
                        case "музыка":
                            if (action.ActionText.IndexOf("включить музыку", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                musicService.Play();
                            }
                            else if (action.ActionText.IndexOf("выключить музыку", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Thread.Sleep(1500);
                                musicService.Stop();
                            }
                            else if (action.ActionText.IndexOf("следующий трек", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                musicService.NextTrack();
                            }
                            break;
                        case "погода":
                                WeatherService weatherService = new WeatherService();
                                int dayOffset = 0;
                                if (action.ActionText.IndexOf("сегодня", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    dayOffset = 0;
                                }
                                else if (action.ActionText.IndexOf("завтра", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    dayOffset = 1;
                                }
                                else if (action.ActionText.IndexOf("послезавтра", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    dayOffset = 2;
                                }

                                await SpeakAsync(weatherService.GetWeatherForecast(dayOffset));
                            break;
                        case "смена имени":
                            if (string.IsNullOrEmpty(action.ActionText))
                            {
                                await SpeakAsync("Пожалуйста, укажите новое имя после команды 'смена имени'");
                            }
                            else
                            {
                                _renameService.BotName = action.ActionText;
                                await SpeakAsync($"Имя успешно изменено на {action.ActionText}");
                            }
                            break;
                    }
                }
            }
        }


        public async Task SpeakAsync(string text)
        {
            int voiceId = 0;
            switch (_settingManager.Setting.VoiceType.ToLower())
            {
                case "maria":
                    voiceId = 18;
                    break;
                case "amber":
                    voiceId = 484;
                    break;
                case "rick sanchez":
                    voiceId = 483;
                    break;
                case "sergey":
                    voiceId = 355;
                    break;
                case "natasha":
                    voiceId = 321;
                    break;
            }
            OnMessageReceived?.Invoke($"Ответ: {text}");
            bool wasMusicPlaying = musicService.IsPlaying();
            if (wasMusicPlaying)
            {
                musicService.Pause();
            }

            try
            {
                var response = await httpClient.PostAsync(synthesisUrl,
                    new StringContent(JsonConvert.SerializeObject(new
                    {
                        voiceId,
                        text
                    }), Encoding.UTF8, "application/json"));
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    OnMessageReceived?.Invoke($"Ошибка синтеза речи: {errorContent}");
                }

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<SynthesisResponse>(responseContent);

                    byte[] audioData = Convert.FromBase64String(result.FileContents);
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "output.mp3");


                    await Task.Run(() => File.WriteAllBytes(filePath, audioData));

                    using (var audioFile = new AudioFileReader(filePath))
                    using (var waveOut = new WaveOutEvent())
                    {
                        waveOut.Volume = _settingManager.Setting.Volume / 10f;
                        waveOut.Init(audioFile);
                        waveOut.Play();

                        while (waveOut.PlaybackState == PlaybackState.Playing)
                        {
                            await Task.Delay(100);
                        }
                    }

                    File.Delete(filePath);
                }
                else
                {
                    OnMessageReceived?.Invoke("Ошибка синтеза речи.");
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка: {ex.Message}");
            }
            finally
            {
                if (wasMusicPlaying)
                {
                    musicService.Resume();
                }
            }
        }
    }

    public class RecognitionResponse
    {
        public Alternative[] Alternatives { get; set; }
    }


    public class Alternative
    {
        public string Text { get; set; }
    }

    public class SynthesisResponse
    {
        public string FileContents { get; set; }
    }
}