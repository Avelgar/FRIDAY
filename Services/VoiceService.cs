using System.Text;
using NAudio.Wave;
using Vosk;
using Newtonsoft.Json;
using System.Net.Http;
using System.IO;
using Friday.Services;
using System.Net.NetworkInformation;
using System.Windows;
using FigmaToWpf;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;

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
        private PoseTrackingService _poseTrackingService;
        private CameraWindow _cameraWindow;
        private bool _isSpeaking = false;
        private string modelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\model"));
        public event Action<string> OnPasswordReceived;
        private readonly VoskRecognizer _recognizer;
        private readonly RenameService _renameService;
        private readonly SettingManager _settingManager;
        private WaveInEvent _waveIn;
        private static MusicService musicService = new MusicService();
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly string apiToken = "6847f156-b1fd-4506-b80e-46b64a14106d";
        private static readonly string synthesisUrl = $"https://public.api.voice.steos.io/api/v1/synthesize-controller/synthesis-by-text?authToken={apiToken}";
        public ListeningState ListeningState { get; private set; }

        public event Action<string> OnMessageReceived;

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

                            // Проверяем, не активен ли сейчас режим скриншота (темный экран)
                            bool isScreenshotModeActive = ScreenshotForm.IsActive;

                            if (recognizedText == _renameService.BotName.ToLower() && !isScreenshotModeActive)
                            {
                                OnMessageReceived?.Invoke($"Распознано: {recognizedText}");
                                await SpeakAsync("Слушаю вас");
                                ListeningState.StartListening();
                                isListeningForCommands = true;
                                lastRecognizedText = string.Empty;
                            }
                            else if (isListeningForCommands && !string.IsNullOrEmpty(recognizedText) && recognizedText != lastRecognizedText)
                            {
                                await ProcessCommand(recognizedText);
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
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при запуске записи: {ex.Message}");
            }
        }

        public async Task ProcessAction(Actions action)
        {
            switch (action.ActionType.ToLower())
            {
                case "голосовой ответ":

                    await SpeakAsync(action.ActionText);
                    break;

                case "открытие файла":
                    var openservice = new AppProcessService();
                    openservice.OpenFile(action.ActionText);
                    break;

                case "завершение процесса":
                    var appprocessservice = new AppProcessService();
                    appprocessservice.KillProcess(action.ActionText);
                    break;

                case "изменение громкости":
                    var setvolumessservice = new AppProcessService();
                    setvolumessservice.SetVolume(action.ActionText);
                    break;

                case "изменение яркости":
                    var setbrightnessservice = new AppProcessService();
                    setbrightnessservice.SetBrightness(action.ActionText);
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

                case "режим камеры":
                    await StartCameraMode();
                    break;

                case "выключить режим камеры":
                    StopCameraMode();
                    break;

                case "скриншот":
                    TakeScreenshot();
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
                    else if (action.ActionText.IndexOf("предыдущий трек", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        musicService.PreviousTrack();
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
                    _renameService.BotName = action.ActionText;
                    await SpeakAsync($"Имя успешно изменено на {_renameService.BotName}");
                    break;
            }

        }


        private async Task StartCameraMode()
        {
            try
            {
                if (_poseTrackingService == null)
                {
                    _poseTrackingService = new PoseTrackingService();
                    _poseTrackingService.OnImageUpdated += UpdateCameraWindow;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _cameraWindow = new CameraWindow(_poseTrackingService.CameraBitmap);
                        _cameraWindow.Closed += (s, e) => StopCameraMode();
                        _cameraWindow.Show();
                    });

                    await Task.Run(() => _poseTrackingService.StartTracking());
                    await SpeakAsync("Режим камеры активирован");
                }
                else
                {
                    await SpeakAsync("Режим камеры уже активен");
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при запуске камеры: {ex.Message}");
                await SpeakAsync("Не удалось запустить режим камеры");
            }
        }

        private void UpdateCameraWindow(WriteableBitmap image)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_cameraWindow != null && _cameraWindow.IsVisible)
                {
                    _cameraWindow.UpdateImage(image);
                }
            });
        }

        private void StopCameraMode()
        {
            try
            {
                if (_poseTrackingService != null)
                {
                    _poseTrackingService.OnImageUpdated -= UpdateCameraWindow;
                    _poseTrackingService.Dispose();
                    _poseTrackingService = null;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_cameraWindow != null)
                    {
                        _cameraWindow.Close();
                        _cameraWindow = null;
                    }
                });
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при остановке камеры: {ex.Message}");
            }
        }



        private void TakeScreenshot()
        {
            using (ScreenshotForm screenshotForm = new ScreenshotForm(this))
            {
                screenshotForm.OnMessageReceived += (message) => OnMessageReceived?.Invoke(message);
                screenshotForm.ShowDialog();

                if (screenshotForm.IsCancelled)
                {
                    OnMessageReceived?.Invoke("Скриншот отменен.");
                    return;
                }
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
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при остановке записи: {ex.Message}");
            }
        }

        public static string GetMacAddress()
        {
            // Получаем все сетевые интерфейсы
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            // Ищем первый активный интерфейс с физическим адресом
            foreach (NetworkInterface networkInterface in networkInterfaces)
            {
                // Пропускаем интерфейсы, которые не работают (не активны) или не имеют физического адреса
                if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                    !string.IsNullOrEmpty(networkInterface.GetPhysicalAddress().ToString()))
                {
                    // Получаем MAC-адрес и форматируем его с дефисами
                    string macAddress = networkInterface.GetPhysicalAddress().ToString();
                    if (macAddress.Length == 12) // Стандартная длина MAC без разделителей
                    {
                        return string.Join("-", Enumerable.Range(0, 6)
                            .Select(i => macAddress.Substring(i * 2, 2)));
                    }
                    return macAddress; // Если уже есть разделители, возвращаем как есть
                }
            }

            return string.Empty; // Возвращаем пустую строку, если не нашли MAC-адрес
        }

        public async Task ProcessCommand(string command)
        {
            OnMessageReceived?.Invoke($"Вы: {command}");

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
                try
                {
                    var message = new
                    {
                        command = command,
                        mac = GetMacAddress(),
                        timestamp = DateTime.Now,
                        name = _renameService.BotName
                    };

                    ((App)Application.Current).SendWebSocketMessage(message);
                }
                catch (Exception ex)
                {
                    OnMessageReceived?.Invoke($"Ошибка при отправке команды: {ex.Message}");
                }
            }
        }

        public async Task SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                OnMessageReceived?.Invoke("Текст для озвучивания отсутствует");
                return;
            }

            //var voices = GetAvailableVoices();
            //OnMessageReceived?.Invoke("Доступные голоса: " + string.Join(", ", voices));

            while (_isSpeaking)
            {
                await Task.Delay(100);
            }

            _isSpeaking = true;
            OnMessageReceived?.Invoke($"Бот: {text}");

            bool wasMusicPlaying = musicService.IsPlaying();
            if (wasMusicPlaying)
            {
                musicService.Pause();
            }

            try
            {
                using (var synthesizer = new SpeechSynthesizer())
                {
                    VoiceInfo selectedVoice = null;
                    string voiceType = _settingManager.Setting.VoiceType;

                    // Получаем все доступные голоса
                    var installedVoices = synthesizer.GetInstalledVoices()
                        .Where(v => v.Enabled)
                        .Select(v => v.VoiceInfo)
                        .ToList();

                    // Попробуем найти точное совпадение
                    selectedVoice = installedVoices.FirstOrDefault(v =>
                        v.Name.Equals(voiceType, StringComparison.OrdinalIgnoreCase));

                    // Если не нашли - попробуем частичное совпадение
                    if (selectedVoice == null)
                    {
                        selectedVoice = installedVoices.FirstOrDefault(v =>
                            v.Name.Contains(voiceType, StringComparison.OrdinalIgnoreCase));
                    }

                    // Если русский голос не найден - используем первый доступный русский
                    if (selectedVoice == null)
                    {
                        selectedVoice = installedVoices.FirstOrDefault(v =>
                            v.Culture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase));
                    }

                    // Если русских нет - используем первый доступный
                    if (selectedVoice == null)
                    {
                        selectedVoice = installedVoices.FirstOrDefault();

                        if (selectedVoice == null)
                        {
                            OnMessageReceived?.Invoke("Нет доступных голосов");
                            return;
                        }

                        OnMessageReceived?.Invoke($"Голос '{voiceType}' не найден. Используется {selectedVoice.Name}");
                    }
                    else
                    {
                        //OnMessageReceived?.Invoke($"Используется голос: {selectedVoice.Name}");
                    }

                    // Выбираем голос
                    synthesizer.SelectVoice(selectedVoice.Name);

                    // Устанавливаем громкость (0-100)
                    synthesizer.Volume = (int)(_settingManager.Setting.Volume * 10);

                    // Асинхронное произношение с ожиданием завершения
                    var tcs = new TaskCompletionSource<bool>();
                    synthesizer.SpeakCompleted += (s, e) => tcs.SetResult(true);
                    synthesizer.SpeakAsync(text);

                    await tcs.Task;
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при озвучивании: {ex.Message}");
            }
            finally
            {
                _isSpeaking = false;
                if (wasMusicPlaying)
                {
                    musicService.Resume();
                }
            }
        }

        // Вспомогательный метод для проверки доступных голосов
        public List<string> GetAvailableVoices()
        {
            var voices = new List<string>();

            try
            {
                using (var synth = new SpeechSynthesizer())
                {
                    foreach (var voice in synth.GetInstalledVoices().Where(v => v.Enabled))
                    {
                        voices.Add($"{voice.VoiceInfo.Name} ({voice.VoiceInfo.Culture.Name}, {voice.VoiceInfo.Gender})");
                    }
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при получении голосов: {ex.Message}");
            }

            return voices;
        }
        public class Actions
        {
            public string ActionType { get; set; }
            public string ActionText { get; set; }
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