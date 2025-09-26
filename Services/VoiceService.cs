using FigmaToWpf;
using Friday.Games;
using Friday.Services;
using NAudio.Wave;
using Newtonsoft.Json;
using System.Drawing; // Для работы со скриншотами
using System.Drawing.Drawing2D;
using System.Drawing.Imaging; // Для формата изображения
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Speech.Synthesis;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vosk;
using static FigmaToWpf.MainWindow;


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
        private readonly BluetoothService _bluetoothService;
        private SpeechSynthesizer _currentSynthesizer;
        private readonly List<string> _stopWords = new List<string> { "стоп", "хватит", "довольно", "заткнись", "завали ебало", "заткнись", "закрой рот"};
        private PoseTrackingService _poseTrackingService;
        private CameraWindow _cameraWindow;
        private bool _isSpeaking = false;
        private string modelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\model"));
        public event Action<string> OnPasswordReceived;
        private readonly VoskRecognizer _recognizer;    
        private readonly RenameService _renameService;
        private readonly SettingManager _settingManager;
        private readonly ChangeVoiceService _changeVoiceService;
        private WaveInEvent _waveIn;
        private static MusicService musicService = new MusicService();
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly string apiToken = "6847f156-b1fd-4506-b80e-46b64a14106d";
        private static readonly string synthesisUrl = $"https://public.api.voice.steos.io/api/v1/synthesize-controller/synthesis-by-text?authToken={apiToken}";
        public ListeningState ListeningState { get; private set; }

        public event Action<string> OnMessageReceived;
        private readonly MainWindow _mainWindow;
        public AttachedFile AttachedFile { get; set; }

        public bool IsScreenshotEnabled { get; set; } = false;

        public VoiceService(RenameService renameService, SettingManager settingManager, MainWindow mainWindow)
        {
            _bluetoothService = new BluetoothService();
            _renameService = renameService;
            _settingManager = settingManager;
            _changeVoiceService = new ChangeVoiceService(settingManager);
            _mainWindow = mainWindow;

            Vosk.Vosk.SetLogLevel(-1);
            Model model = new Model(modelPath);
            _recognizer = new VoskRecognizer(model, 16000.0f);
            _recognizer.SetMaxAlternatives(1);
            _recognizer.SetWords(true);

            ListeningState = new ListeningState();
        }

        public async Task StartListening()
        {
            try
            {
                _waveIn = new WaveInEvent();
                _waveIn.WaveFormat = new WaveFormat(16000, 1);

                if (WaveIn.DeviceCount == 0)
                {
                    await DispatchToUI(() => OnMessageReceived?.Invoke("Нет доступных устройств для записи."));
                    return;
                }

                _waveIn.DeviceNumber = 0;
                string lastRecognizedText = string.Empty;
                bool isListeningForCommands = false;

                _waveIn.DataAvailable += (sender, e) =>
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

                            // Получаем текущий режим ввода в UI-потоке
                            InputMode inputMode = InputMode.NameResponseCommand;
                            bool isScreenshotModeActive = ScreenshotForm.IsActive;

                            _mainWindow.Dispatcher.Invoke(() =>
                            {
                                inputMode = (InputMode)_mainWindow.InputModeComboBox.SelectedIndex;
                            });
                            if (_isSpeaking)
                            {
                                // Проверяем содержит ли речь пользователя стоп-слово
                                if (!string.IsNullOrEmpty(recognizedText) &&
                                    _stopWords.Any(stopWord => recognizedText.ToLower().Contains(stopWord)))
                                {
                                    // Получаем последний ответ бота из App
                                    var app = (App)System.Windows.Application.Current;
                                    string lastAnswer = app._last_answer.ToLower();

                                    // Проверяем нет ли стоп-слова в ответе бота
                                    if (!_stopWords.Any(stopWord => lastAnswer.Contains(stopWord)))
                                    {
                                        // Прерываем голосовой ответ
                                        StopSpeaking();
                                    }
                                }
                            }
                            else
                            {
                                // Обработка в зависимости от режима ввода
                                switch (inputMode)
                                {
                                    case InputMode.NameResponseCommand:
                                        if (recognizedText == _renameService.BotName.ToLower() && !isScreenshotModeActive)
                                        {
                                            OnMessageReceived?.Invoke($"Вы: {recognizedText}");
                                            _ = SpeakAsync("Бот", "Слушаю вас"); // Используем discard для асинхронного вызова
                                            ListeningState.StartListening();
                                            isListeningForCommands = true;
                                            lastRecognizedText = string.Empty;
                                        }
                                        else if (isListeningForCommands && !string.IsNullOrEmpty(recognizedText) && recognizedText != lastRecognizedText)
                                        {
                                            _ = ProcessCommand(recognizedText); // Используем discard для асинхронного вызова
                                            isListeningForCommands = false;
                                            lastRecognizedText = recognizedText;
                                        }
                                        break;

                                    case InputMode.NamePlusCommand:
                                        if (!isScreenshotModeActive && recognizedText != null &&
                                            recognizedText.ToLower().Contains(_renameService.BotName.ToLower()))
                                        {

                                            if (!string.IsNullOrEmpty(recognizedText))
                                            {
                                                _ = ProcessCommand(recognizedText); // Используем discard для асинхронного вызова
                                            }
                                        }
                                        break;

                                    case InputMode.Conversation:
                                        if (!isScreenshotModeActive && !string.IsNullOrEmpty(recognizedText) &&
                                            recognizedText != lastRecognizedText)
                                        {
                                            _ = ProcessCommand(recognizedText); // Используем discard для асинхронного вызова
                                            lastRecognizedText = recognizedText;
                                        }
                                        break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DispatchToUI(() => OnMessageReceived?.Invoke($"Ошибка при обработке аудиоданных: {ex.Message}"));
                    }
                };

                _waveIn.StartRecording();
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                await DispatchToUI(() => OnMessageReceived?.Invoke($"Ошибка при запуске записи: {ex.Message}"));
            }
        }

        public void StopSpeaking()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _currentSynthesizer?.SpeakAsyncCancelAll();
                _currentSynthesizer?.Dispose();
                _currentSynthesizer = null;
                _isSpeaking = false;
            });
        }

        // Вспомогательный метод для вызова в UI-потоке
        private async Task DispatchToUI(Action action)
        {
            await _mainWindow.Dispatcher.InvokeAsync(action);
        }

        public async Task ProcessAction(Actions action)
        {
            switch (action.ActionType.ToLower())
            {
                case "голосовой ответ":
                    // Передаем отправителя и текст
                    await SpeakAsync(action.Sender, action.ActionText);
                    break;

                case "текстовой ответ":
                    OnMessageReceived?.Invoke($"{action.Sender}: {action.ActionText}");
                    break;

                case "очистка истории":
                    _mainWindow.ClearHistory();
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
                case "переместить мышь":
                    var movemouseService = new MouseService();
                    movemouseService.MoveMouse(action.ActionText);
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

                    await SpeakAsync("Бот", weatherService.GetWeatherForecast(dayOffset));
                    break;
                case "смена имени":
                    _renameService.BotName = action.ActionText;
                    _settingManager.Setting.AssistantName = action.ActionText;
                    _settingManager.SaveSettings();
                    break;

                case "смена голоса":
                    _changeVoiceService.ChangeVoice(action.ActionText);
                    break;

                case "управление блютусом":
                    if (action.ActionText.IndexOf("включить", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _bluetoothService.SetBluetoothState("включить");
                    }
                    else if (action.ActionText.IndexOf("выключить", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _bluetoothService.SetBluetoothState("выключить");
                    }
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

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        _cameraWindow = new CameraWindow(_poseTrackingService.CameraBitmap);
                        _cameraWindow.Closed += (s, e) => StopCameraMode();
                        _cameraWindow.Show();
                    });

                    await Task.Run(() => _poseTrackingService.StartTracking());
                    await SpeakAsync("Бот", "Режим камеры активирован");
                }
                else
                {
                    await SpeakAsync("Бот", "Режим камеры уже активен");
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при запуске камеры: {ex.Message}");
                await SpeakAsync("Бот", "Не удалось запустить режим камеры");
            }
        }

        private void UpdateCameraWindow(WriteableBitmap image)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
                    string screenshotBase64 = null;

                    // Получаем прикрепленный файл из MainWindow
                    var attachedFile = _mainWindow.GetAttachedFile();

                    // Приоритет: сначала проверяем прикрепленный файл
                    if (attachedFile != null)
                    {
                        screenshotBase64 = Convert.ToBase64String(attachedFile.Data);
                    }
                    // Если файла нет, но включен режим скриншота - делаем скриншот
                    else if (IsScreenshotEnabled)
                    {
                        byte[] screenshotBytes = CaptureScreenshot();
                        if (screenshotBytes != null)
                        {
                            screenshotBase64 = Convert.ToBase64String(screenshotBytes);
                        }
                    }

                    var message = new
                    {
                        type = "голосовое сообщение",
                        command = command,
                        mac = GetMacAddress(),
                        timestamp = DateTime.Now,
                        name = _renameService.BotName,
                        screenshot = screenshotBase64
                    };

                    ((App)System.Windows.Application.Current).SendWebSocketMessage(message);

                    // Очищаем прикрепленный файл после отправки
                    System.Windows.Application.Current.Dispatcher.Invoke(() => _mainWindow.ClearAttachedFile());
                }
                catch (Exception ex)
                {
                    OnMessageReceived?.Invoke($"Ошибка при отправке команды: {ex.Message}");
                }
            }
        }

        public byte[]? CaptureScreenshot()
        {
            try
            {
                Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault()
                                    ?? throw new InvalidOperationException("Не удалось определить экран для скриншота.");
                Rectangle bounds = primaryScreen.Bounds;

                using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        // Копируем экран
                        g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);

                        // Настраиваем рендеринг для качественной графики
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                        // Рисуем координатную сетку и разметку
                        DrawCoordinateMarkings(g, bounds);
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        bitmap.Save(ms, ImageFormat.Png);
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при создании скриншота: {ex.Message}");
                return null;
            }
        }

        private void DrawCoordinateMarkings(Graphics g, Rectangle bounds)
        {
            using (Pen gridPen = new Pen(Color.FromArgb(120, Color.Red), 1))
            using (Font coordFont = new Font("Arial", 10))
            using (Brush textBrush = new SolidBrush(Color.White))
            using (Brush backgroundBrush = new SolidBrush(Color.FromArgb(100, Color.Black)))
            {
                // Рисуем сетку с шагом 100 пикселей
                for (int x = 0; x < bounds.Width; x += 50)
                {
                    g.DrawLine(gridPen, x, 0, x, bounds.Height);
                    DrawTextWithBackground(g, $"{x}", coordFont, textBrush, backgroundBrush, x, 5);
                }

                for (int y = 0; y < bounds.Height; y += 50)
                {
                    g.DrawLine(gridPen, 0, y, bounds.Width, y);
                    DrawTextWithBackground(g, $"{y}", coordFont, textBrush, backgroundBrush, 5, y);
                }

                // Угловые координаты
                string[] corners = {
                    $"({0}, {0})",
                    $"({bounds.Width}, {0})",
                    $"({0}, {bounds.Height})",
                    $"({bounds.Width}, {bounds.Height})"
                };

                System.Drawing.Point[] points = {
                    new System.Drawing.Point(10, 10),
                    new System.Drawing.Point(bounds.Width - 120, 10),
                    new System.Drawing.Point(10, bounds.Height - 30),
                    new System.Drawing.Point(bounds.Width - 120, bounds.Height - 30)
                };

                for (int i = 0; i < corners.Length; i++)
                {
                    DrawTextWithBackground(g, corners[i], coordFont, textBrush, backgroundBrush, points[i].X, points[i].Y);
                }
            }
        }

        private void DrawTextWithBackground(Graphics g, string text, Font font, Brush textBrush, Brush backgroundBrush, int x, int y)
        {
            SizeF textSize = g.MeasureString(text, font);
            RectangleF backgroundRect = new RectangleF(x, y, textSize.Width + 4, textSize.Height + 2);

            g.FillRectangle(backgroundBrush, backgroundRect);
            g.DrawString(text, font, textBrush, x + 2, y + 1);
        }

        public async Task SpeakAsync(string sender, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                OnMessageReceived?.Invoke("Текст для озвучивания отсутствует");
                return;
            }

            while (_isSpeaking)
            {
                await Task.Delay(100);
            }

            _isSpeaking = true;
            OnMessageReceived?.Invoke($"{sender}: {text}");

            bool wasMusicPlaying = musicService.IsPlaying();
            if (wasMusicPlaying)
            {
                musicService.Pause();
            }

            try
            {
                _currentSynthesizer = new SpeechSynthesizer(); // Используем поле

                VoiceInfo selectedVoice = null;
                string voiceType = _settingManager.Setting.VoiceType;

                // Получаем все доступные голоса
                var installedVoices = _currentSynthesizer.GetInstalledVoices()
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
                _currentSynthesizer.SelectVoice(selectedVoice.Name);

                // Устанавливаем громкость (0-100)
                _currentSynthesizer.Volume = (int)(_settingManager.Setting.Volume * 10);

                // Асинхронное произношение с ожиданием завершения
                var tcs = new TaskCompletionSource<bool>();
                _currentSynthesizer.SpeakCompleted += (s, e) => tcs.SetResult(true);
                _currentSynthesizer.SpeakAsync(text);

                await tcs.Task;
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при озвучивании: {ex.Message}");
            }
            finally
            {
                _currentSynthesizer?.Dispose();
                _currentSynthesizer = null;
                await Task.Delay(100);
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
            public string Sender { get; set; } // Новое свойство
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

