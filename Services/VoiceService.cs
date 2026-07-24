using Friday.Services;
using NAudio.Wave;
using Newtonsoft.Json;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Vosk;

namespace Friday
{
    public class VoiceService
    {
        private readonly List<string> _stopWords = new List<string> { "стоп", "хватит", "довольно", "заткнись", "закрой рот" };

        private bool _isSpeaking = false;
        private bool _isWaitingForServer = false;
        private bool _isRecordingCommand = false;
        private string _currentCommandMsgId = null;

        private string modelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\model"));

        private WaveOutEvent _waveOut;
        private readonly VoskRecognizer _recognizer;
        private readonly RenameService _renameService;
        private SettingManager _settingManager;
        private readonly ChangeVoiceService _changeVoiceService;
        private WaveInEvent _waveIn;
        public static MusicService musicService;
        public ListeningState ListeningState { get; private set; }

        public event Action<ChatMessage> OnChatMessageReceived;
        private readonly MainWindow _mainWindow;
        public MainWindow.AttachedFile AttachedFile { get; set; }

        public bool IsScreenshotEnabled { get; set; } = false;

        private BufferedWaveProvider _waveProvider;
        private bool _isAudioStreamPlaying = false;
        private readonly object _audioLock = new object();

        // БУФЕР ДЛЯ ЗАПИСИ ГОЛОСА
        private MemoryStream _audioBuffer = new MemoryStream();

        public VoiceService(RenameService renameService, SettingManager settingManager, MainWindow mainWindow)
        {
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
            musicService = new MusicService();
            musicService.Init();
        }

        private void InitAudioStream()
        {
            if (_waveOut == null)
            {
                _waveOut = new WaveOutEvent();
                _waveProvider = new BufferedWaveProvider(new WaveFormat(24000, 16, 1));
                _waveProvider.BufferDuration = TimeSpan.FromMinutes(2);
                _waveProvider.DiscardOnBufferOverflow = true;
                _waveOut.Init(_waveProvider);
            }
        }

        public async Task StartListening()
        {
            try
            {
                if (WaveIn.DeviceCount == 0)
                {
                    _mainWindow.ShowSystemMessage("Нет доступных устройств для записи.");
                    return;
                }

                int selectedDeviceIndex = 0;
                Console.WriteLine("=== ДОСТУПНЫЕ МИКРОФОНЫ ===");
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    var caps = WaveIn.GetCapabilities(i);
                    Console.WriteLine($"[{i}] {caps.ProductName}");
                }

                _waveIn = new WaveInEvent()
                {
                    WaveFormat = new WaveFormat(16000, 1),
                    DeviceNumber = selectedDeviceIndex
                };

                string lastRecognizedText = string.Empty;

                _waveIn.DataAvailable += (sender, e) =>
                {
                    try
                    {
                        if (_isSpeaking)
                        {
                            if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                            {
                                var res = JsonConvert.DeserializeObject<RecognitionResponse>(_recognizer.Result());
                                if (res?.Alternatives.FirstOrDefault()?.Text is string text && _stopWords.Any(w => text.ToLower().Contains(w)))
                                    StopSpeaking();
                            }
                            else
                            {
                                var partialRes = JsonConvert.DeserializeObject<RecognitionPartialResponse>(_recognizer.PartialResult());
                                if (partialRes?.Partial is string text && _stopWords.Any(w => text.ToLower().Contains(w)))
                                    StopSpeaking();
                            }
                            return;
                        }

                        if (_isWaitingForServer) return;

                        var partialResponse = JsonConvert.DeserializeObject<RecognitionPartialResponse>(_recognizer.PartialResult());
                        string partialText = partialResponse?.Partial?.ToLower() ?? "";

                        // Пишем звук в наш буфер
                        _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);

                        // === УМНОЕ ОБРЕЗАНИЕ ТИШИНЫ (СКОЛЬЗЯЩЕЕ ОКНО) ===
                        if (string.IsNullOrEmpty(partialText) && !_isRecordingCommand)
                        {
                            // 1 секунда аудио при 16000Hz 16-bit Mono весит ровно 32000 байт.
                            // Мы всегда держим последнюю 1 секунду звука (пре-буфер).
                            const int PreBufferSize = 32000;

                            if (_audioBuffer.Length > PreBufferSize)
                            {
                                byte[] allBytes = _audioBuffer.ToArray();
                                _audioBuffer.SetLength(0); // Сбрасываем позицию записи

                                // Возвращаем в буфер только последнюю секунду звука
                                _audioBuffer.Write(allBytes, allBytes.Length - PreBufferSize, PreBufferSize);
                            }
                        }
                        else
                        {
                            _isRecordingCommand = true; // Фиксируем старт реального говорения!
                        }

                        // VOSK ОПРЕДЕЛИЛ КОНЕЦ ФРАЗЫ
                        if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded) && _isRecordingCommand)
                        {
                            _isRecordingCommand = false;

                            var result = _recognizer.Result();
                            var response = JsonConvert.DeserializeObject<RecognitionResponse>(result);
                            string recognizedText = response?.Alternatives.FirstOrDefault()?.Text?.ToLower() ?? "";

                            // Забираем чистый звук (вместе с бережно сохраненным первым словом!)
                            byte[] pcmData = _audioBuffer.ToArray();
                            _audioBuffer.SetLength(0); // Очищаем буфер перед следующей командой

                            if (string.IsNullOrEmpty(recognizedText)) return;

                            InputMode inputMode = InputMode.NamePlusCommand;
                            _mainWindow.Dispatcher.Invoke(() => {
                                inputMode = (InputMode)_mainWindow.InputModeComboBox.SelectedIndex;
                            });

                            string botName = _renameService.BotName.ToLower();
                            bool shouldSend = false;

                            if (inputMode == InputMode.NamePlusCommand && recognizedText.Contains(botName))
                            {
                                shouldSend = true;
                            }
                            else if (inputMode == InputMode.Conversation)
                            {
                                shouldSend = true;
                            }

                            if (shouldSend)
                            {
                                _isWaitingForServer = true;
                                _currentCommandMsgId = Guid.NewGuid().ToString();
                                _ = SendAudioCommand(pcmData);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка аудио: {ex.Message}");
                    }
                };

                _waveIn.StartRecording();
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex) { _mainWindow.ShowSystemMessage($"Ошибка микрофона: {ex.Message}"); }
        }

        public async Task SendAudioCommand(byte[] pcmData)
        {
            var app = (App)Application.Current;
            if (app.IsWaitingForServerResponse) return;

            string pendingMsgId = Guid.NewGuid().ToString();
            OnChatMessageReceived?.Invoke(new ChatMessage { Id = pendingMsgId, Sender = "Вы", Text = "⏳ Распознавание..." });

            try
            {
                string screenshotBase64 = null;
                var attachedFile = _mainWindow.GetAttachedFile();
                if (attachedFile != null) screenshotBase64 = Convert.ToBase64String(attachedFile.Data);

                string audioBase64 = Convert.ToBase64String(pcmData);

                var message = new
                {
                    type = "голосовое сообщение",
                    command = "",
                    audio_base64 = audioBase64,
                    mac = App.GetMacAddress(),
                    timestamp = DateTime.Now,
                    name = _renameService.BotName,
                    voice_type = SettingManager.Setting.VoiceType,
                    screenshot = screenshotBase64,
                    ui_msg_id = pendingMsgId
                };

                app.SendWebSocketMessage(message);
                System.Windows.Application.Current.Dispatcher.Invoke(() => _mainWindow.ClearAttachedFile());
            }
            catch (Exception ex)
            {
                _mainWindow.ShowSystemMessage($"Ошибка аудио: {ex.Message}");
                _isWaitingForServer = false;
            }
        }

        public void AppendAudioChunk(string base64Audio)
        {
            if (string.IsNullOrEmpty(base64Audio)) return;

            InitAudioStream();
            byte[] pcmData = Convert.FromBase64String(base64Audio);
            _waveProvider.AddSamples(pcmData, 0, pcmData.Length);

            lock (_audioLock)
            {
                if (!_isAudioStreamPlaying)
                {
                    _isAudioStreamPlaying = true;
                    _isSpeaking = true;

                    bool wasMusicPlaying = musicService.IsPlaying();
                    if (wasMusicPlaying) musicService.Pause();

                    _waveOut.Play();

                    Task.Run(async () =>
                    {
                        while (_isAudioStreamPlaying)
                        {
                            if (_waveProvider.BufferedBytes == 0)
                            {
                                await Task.Delay(300);
                                if (_waveProvider.BufferedBytes == 0)
                                {
                                    _waveOut.Stop();
                                    _isAudioStreamPlaying = false;
                                    _isSpeaking = false;
                                    _isWaitingForServer = false;

                                    if (wasMusicPlaying) musicService.Resume();
                                    break;
                                }
                            }
                            await Task.Delay(100);
                        }
                    });
                }
            }
        }

        public void ShowTextInChat(string sender, string text, string messageId = null)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                OnChatMessageReceived?.Invoke(new ChatMessage { Id = messageId ?? Guid.NewGuid().ToString(), Sender = sender, Text = text });
            }
        }

        public void PlayNativeAudio(string base64Audio)
        {
            try
            {
                byte[] pcmData = Convert.FromBase64String(base64Audio);
                var format = new WaveFormat(24000, 16, 1);
                using (var ms = new MemoryStream(pcmData))
                using (var provider = new RawSourceWaveStream(ms, format))
                using (var waveOut = new WaveOutEvent())
                {
                    waveOut.Init(provider);
                    _isSpeaking = true;
                    waveOut.Play();
                    while (waveOut.PlaybackState == PlaybackState.Playing) Thread.Sleep(100);
                    _isSpeaking = false;
                    _isWaitingForServer = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                _isWaitingForServer = false;
            }
        }

        public void StopListening() { try { if (_waveIn != null) { _waveIn.StopRecording(); _waveIn.Dispose(); _waveIn = null; } } catch { } }
        public void StopSpeaking() => _isSpeaking = false;

        public void ResetWaitingForServer()
        {
            _isWaitingForServer = false;
        }

        public async Task ProcessAction(Actions action)
        {
            if (action == null || string.IsNullOrEmpty(action.ActionType)) return;
            string safeActionText = action.ActionText ?? string.Empty;
            switch (action.ActionType.ToLower())
            {
                case "очистка истории": _mainWindow.ClearHistory(); break;
                case "открытие файла": new AppProcessService().OpenFile(safeActionText); break;
                case "завершение процесса": new AppProcessService().KillProcess(safeActionText); break;
                case "изменение громкости": new AppProcessService().SetVolume(safeActionText); break;
                case "изменение яркости": new AppProcessService().SetBrightness(safeActionText); break;
                case "открытие ссылки": new BrowserService().OpenLink(safeActionText); break;
                case "напечатать текст": new KeyboardService().TypeText(safeActionText); break;
                case "уведомление": new NotificationService().SendNotification(safeActionText); break;
                case "нажать кнопку мыши": new MouseService().PressMouseButton(safeActionText); break;
                case "переместить мышь": new MouseService().MoveMouse(safeActionText); break;
                case "смена имени":
                    _renameService.BotName = safeActionText;
                    SettingManager.Setting.AssistantName = safeActionText;
                    _settingManager.SaveSettings();
                    break;
                case "смена голоса": _changeVoiceService.ChangeVoice(safeActionText); break;
                case "музыка":
                    try
                    {
                        if (safeActionText.IndexOf("включить", StringComparison.OrdinalIgnoreCase) >= 0) musicService.Play();
                        else if (safeActionText.IndexOf("выключить", StringComparison.OrdinalIgnoreCase) >= 0) { Thread.Sleep(1500); musicService.Stop(); }
                        else if (safeActionText.IndexOf("следующий", StringComparison.OrdinalIgnoreCase) >= 0) musicService.NextTrack();
                        else if (safeActionText.IndexOf("предыдущий", StringComparison.OrdinalIgnoreCase) >= 0) musicService.PreviousTrack();
                    }
                    catch (Exception) { }
                    break;
            }
        }

        public class Actions
        {
            public string ActionType { get; set; }
            public string ActionText { get; set; }
            public string Sender { get; set; }
            public string MessageId { get; set; }
            public string UserMsgId { get; set; }
            public bool IsLocal { get; set; } = true;
            public string AudioBase64 { get; set; }
        }
    }

    public class RecognitionResponse { public Alternative[] Alternatives { get; set; } }
    public class RecognitionPartialResponse { public string Partial { get; set; } }
    public class Alternative { public string Text { get; set; } }
}