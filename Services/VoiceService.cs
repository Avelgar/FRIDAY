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
        private bool _isMutedByStopWord = false;
        private string _currentCommandMsgId = null;
        private InputMode _currentInputMode = InputMode.NamePlusCommand;

        private DateTime _lastSpeechTime = DateTime.MinValue;
        private DateTime _ignoreCommandsUntil = DateTime.MinValue;
        private DateTime _lastDebugNotificationTime = DateTime.MinValue;

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

                _waveIn = new WaveInEvent()
                {
                    WaveFormat = new WaveFormat(16000, 1),
                    DeviceNumber = 0
                };

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

                        if (DateTime.Now < _ignoreCommandsUntil)
                        {
                            if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded)) _recognizer.Result();
                            return;
                        }

                        bool isPhraseComplete = _recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded);
                        var partialResponse = JsonConvert.DeserializeObject<RecognitionPartialResponse>(_recognizer.PartialResult());
                        string partialText = partialResponse?.Partial?.ToLower() ?? "";

                        float sum = 0;
                        int sampleCount = e.BytesRecorded / 2;
                        for (int i = 0; i < e.BytesRecorded; i += 2)
                        {
                            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                            float s = sample / 32768f;
                            sum += Math.Abs(s);
                        }
                        float volume = sampleCount > 0 ? sum / sampleCount : 0;

                        if (volume > 0.005f)
                        {
                            _lastSpeechTime = DateTime.Now;
                        }

                        if (!_isRecordingCommand)
                        {
                            _mainWindow.Dispatcher.Invoke(() => {
                                _currentInputMode = (InputMode)_mainWindow.InputModeComboBox.SelectedIndex;
                            });
                        }

                        if (_currentInputMode == InputMode.Conversation)
                        {
                            if (_isRecordingCommand)
                            {
                                byte[] chunk = new byte[e.BytesRecorded];
                                Array.Copy(e.Buffer, chunk, e.BytesRecorded);
                                SendStreamChunk(_currentCommandMsgId, chunk);

                                if ((DateTime.Now - _lastSpeechTime).TotalMilliseconds > 1500)
                                {
                                    _isRecordingCommand = false;
                                    _isWaitingForServer = true;
                                    SendStreamEnd(_currentCommandMsgId);
                                    _currentCommandMsgId = null;

                                    if (!isPhraseComplete) _recognizer.Result();
                                }
                            }
                            else
                            {
                                _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);

                                if (string.IsNullOrEmpty(partialText))
                                {
                                    const int PreBufferSize = 32000;
                                    if (_audioBuffer.Length > PreBufferSize)
                                    {
                                        byte[] allBytes = _audioBuffer.ToArray();
                                        _audioBuffer.SetLength(0);
                                        _audioBuffer.Write(allBytes, allBytes.Length - PreBufferSize, PreBufferSize);
                                    }
                                }
                                else
                                {
                                    _isRecordingCommand = true;
                                    _lastSpeechTime = DateTime.Now;
                                    _currentCommandMsgId = Guid.NewGuid().ToString();

                                    _mainWindow.Dispatcher.Invoke(() => {
                                        OnChatMessageReceived?.Invoke(new ChatMessage { Id = _currentCommandMsgId, UiMsgId = _currentCommandMsgId, Sender = "Вы", Text = "🎤 [Слушаю...]" });
                                    });

                                    byte[] pcmData = _audioBuffer.ToArray();
                                    _audioBuffer.SetLength(0);
                                    SendStreamStart(_currentCommandMsgId, pcmData);
                                }
                            }
                        }
                        else if (_currentInputMode == InputMode.NamePlusCommand)
                        {
                            _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);

                            if (string.IsNullOrEmpty(partialText) && !_isRecordingCommand)
                            {
                                const int PreBufferSize = 32000;
                                if (_audioBuffer.Length > PreBufferSize)
                                {
                                    byte[] allBytes = _audioBuffer.ToArray();
                                    _audioBuffer.SetLength(0);
                                    _audioBuffer.Write(allBytes, allBytes.Length - PreBufferSize, PreBufferSize);
                                }
                            }
                            else if (!string.IsNullOrEmpty(partialText) && !_isRecordingCommand)
                            {
                                _isRecordingCommand = true;
                                _currentCommandMsgId = Guid.NewGuid().ToString();

                                _mainWindow.Dispatcher.Invoke(() => {
                                    OnChatMessageReceived?.Invoke(new ChatMessage { Id = _currentCommandMsgId, UiMsgId = _currentCommandMsgId, Sender = "Вы", Text = "🎤 [Слушаю...]" });
                                });
                            }

                            if (isPhraseComplete && _isRecordingCommand)
                            {
                                _isRecordingCommand = false;

                                var result = _recognizer.Result();
                                var response = JsonConvert.DeserializeObject<RecognitionResponse>(result);
                                string recognizedText = response?.Alternatives.FirstOrDefault()?.Text?.ToLower() ?? "";

                                byte[] pcmData = _audioBuffer.ToArray();
                                _audioBuffer.SetLength(0);

                                string botName = _renameService.BotName.ToLower();

                                if (!string.IsNullOrEmpty(recognizedText) && recognizedText.Contains(botName))
                                {
                                    _isWaitingForServer = true;
                                    SendAudioCommand(pcmData, _currentCommandMsgId);
                                    _currentCommandMsgId = null;
                                }
                                else
                                {
                                    _mainWindow.Dispatcher.Invoke(() => {
                                        var msg = _mainWindow.ChatMessages.FirstOrDefault(m => m.UiMsgId == _currentCommandMsgId);
                                        if (msg != null) _mainWindow.ChatMessages.Remove(msg);
                                    });
                                    _currentCommandMsgId = null;
                                }
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

        public void SendStreamStart(string uiMsgId, byte[] initialAudio)
        {
            string screenshotBase64 = null;
            var attachedFile = _mainWindow.GetAttachedFile();
            if (attachedFile != null) screenshotBase64 = Convert.ToBase64String(attachedFile.Data);

            bool isGuest = !((App)Application.Current).IsLoggedIn;

            // dialog_id передаём всегда (даже null) — см. комментарий в MainWindow.SendCurrentMessageAsync
            var message = new
            {
                type = "голосовое сообщение",
                command = "",
                stream_audio = true,
                audio_base64 = Convert.ToBase64String(initialAudio),
                mac = App.GetMacAddress(),
                timestamp = DateTime.Now,
                name = _renameService.BotName,
                voice_type = SettingManager.Setting.VoiceType,
                screenshot = screenshotBase64,
                ui_msg_id = uiMsgId,
                dialog_id = isGuest ? (long?)null : _mainWindow.CurrentDialogId,
                message_history = isGuest ? _mainWindow.GetGuestMessageHistory() : null
            };
            ((App)Application.Current).SendWebSocketMessage(message);
            System.Windows.Application.Current.Dispatcher.Invoke(() => _mainWindow.ClearAttachedFile());
        }

        public void SendStreamChunk(string uiMsgId, byte[] pcmData)
        {
            var message = new
            {
                type = "audio_stream_chunk",
                ui_msg_id = uiMsgId,
                audio_base64 = Convert.ToBase64String(pcmData)
            };
            ((App)Application.Current).SendWebSocketMessage(message);
        }

        public void SendStreamEnd(string uiMsgId)
        {
            _mainWindow.Dispatcher.Invoke(() => {
                var msg = _mainWindow.ChatMessages.FirstOrDefault(m => m.UiMsgId == uiMsgId);
                if (msg != null && msg.Text == "🎤 [Слушаю...]")
                {
                    msg.Text = "⏳ Транскрибирую...";
                    msg.DisplayText = "⏳ Транскрибирую...";
                }
            });

            var message = new
            {
                type = "audio_stream_end",
                ui_msg_id = uiMsgId
            };
            ((App)Application.Current).SendWebSocketMessage(message);
        }

        public void SendAudioCommand(byte[] pcmData, string providedUiMsgId = null)
        {
            string pendingMsgId = providedUiMsgId ?? Guid.NewGuid().ToString();

            _mainWindow.Dispatcher.Invoke(() => {
                var msg = _mainWindow.ChatMessages.FirstOrDefault(m => m.UiMsgId == pendingMsgId);
                if (msg != null)
                {
                    msg.Text = "⏳ Транскрибирую...";
                    msg.DisplayText = "⏳ Транскрибирую...";
                }
                else
                {
                    OnChatMessageReceived?.Invoke(new ChatMessage { Id = pendingMsgId, UiMsgId = pendingMsgId, Sender = "Вы", Text = "⏳ Транскрибирую..." });
                }
            });

            try
            {
                string screenshotBase64 = null;
                var attachedFile = _mainWindow.GetAttachedFile();
                if (attachedFile != null) screenshotBase64 = Convert.ToBase64String(attachedFile.Data);

                string audioBase64 = Convert.ToBase64String(pcmData);
                bool isGuest = !((App)Application.Current).IsLoggedIn;

                var message = new
                {
                    type = "голосовое сообщение",
                    command = "",
                    audio_base64 = audioBase64,
                    stream_audio = false,
                    mac = App.GetMacAddress(),
                    timestamp = DateTime.Now,
                    name = _renameService.BotName,
                    voice_type = SettingManager.Setting.VoiceType,
                    screenshot = screenshotBase64,
                    ui_msg_id = pendingMsgId,
                    dialog_id = isGuest ? (long?)null : _mainWindow.CurrentDialogId,
                    message_history = isGuest ? _mainWindow.GetGuestMessageHistory() : null
                };

                ((App)Application.Current).SendWebSocketMessage(message);
                System.Windows.Application.Current.Dispatcher.Invoke(() => _mainWindow.ClearAttachedFile());
            }
            catch (Exception ex)
            {
                _mainWindow.ShowSystemMessage($"Ошибка аудио: {ex.Message}");
                lock (_audioLock) { _isWaitingForServer = false; }
            }
        }

        public void AppendAudioChunk(string base64Audio)
        {
            if (_isMutedByStopWord || string.IsNullOrEmpty(base64Audio)) return;

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
                                    lock (_audioLock)
                                    {
                                        try { _waveOut?.Stop(); } catch { }
                                        _isAudioStreamPlaying = false;
                                        _isSpeaking = false;
                                        _isWaitingForServer = false;
                                        _audioBuffer.SetLength(0);
                                    }

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

        public void ShowTextInChat(string sender, string text, string messageId = null, string uiMsgId = null)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                OnChatMessageReceived?.Invoke(new ChatMessage
                {
                    Id = messageId ?? Guid.NewGuid().ToString(),
                    UiMsgId = uiMsgId,
                    Sender = sender,
                    Text = text
                });
            }
        }

        public void PlayNativeAudio(string base64Audio)
        {
            if (_isMutedByStopWord) return;

            try
            {
                byte[] pcmData = Convert.FromBase64String(base64Audio);
                var format = new WaveFormat(24000, 16, 1);
                using (var ms = new MemoryStream(pcmData))
                using (var provider = new RawSourceWaveStream(ms, format))
                using (var waveOut = new WaveOutEvent())
                {
                    waveOut.Init(provider);
                    lock (_audioLock) { _isSpeaking = true; }
                    waveOut.Play();

                    while (waveOut.PlaybackState == PlaybackState.Playing && _isSpeaking) Thread.Sleep(100);

                    lock (_audioLock)
                    {
                        _isSpeaking = false;
                        _isWaitingForServer = false;
                        _audioBuffer.SetLength(0);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                lock (_audioLock) { _isWaitingForServer = false; }
            }
        }

        public void StopListening()
        {
            try
            {
                if (_waveIn != null) { _waveIn.StopRecording(); _waveIn.Dispose(); _waveIn = null; }
                if (_isRecordingCommand)
                {
                    _mainWindow.Dispatcher.Invoke(() => {
                        var msg = _mainWindow.ChatMessages.FirstOrDefault(m => m.UiMsgId == _currentCommandMsgId);
                        if (msg != null) _mainWindow.ChatMessages.Remove(msg);
                    });
                    _isRecordingCommand = false;
                    _currentCommandMsgId = null;
                }
            }
            catch { }
        }

        public void StopSpeaking()
        {
            lock (_audioLock)
            {
                _isMutedByStopWord = true;
                _isSpeaking = false;
                _isWaitingForServer = false;
                _isAudioStreamPlaying = false;

                _ignoreCommandsUntil = DateTime.Now.AddSeconds(1.5);

                try
                {
                    _waveOut?.Stop();
                    _waveProvider?.ClearBuffer();
                }
                catch { }
                _audioBuffer.SetLength(0);
            }
        }

        public void SetWaitingForServer()
        {
            lock (_audioLock)
            {
                _isWaitingForServer = true;
                _audioBuffer.SetLength(0);
            }
        }

        public void ServerFinishedResponse()
        {
            lock (_audioLock)
            {
                _isMutedByStopWord = false;
                _isWaitingForServer = false;
                _audioBuffer.SetLength(0);
            }
        }

        public async Task ProcessAction(Actions action)
        {
            if (action == null || string.IsNullOrEmpty(action.ActionType)) return;
            string safeActionText = action.ActionText ?? string.Empty;
            switch (action.ActionType.ToLower())
            {
                case "очистка истории":
                    // Сервер вырезает этот action, когда работа идёт в диалоге аккаунта
                    // (dialog_id != null), так что сюда он долетает только у гостя.
                    // Дублируем проверку на случай рассинхрона версий клиента и сервера.
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (((App)Application.Current).IsLoggedIn) return;
                        _mainWindow.ClearHistory();
                    });
                    break;
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