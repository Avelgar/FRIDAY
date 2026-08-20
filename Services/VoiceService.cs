using Friday.Services;
using NAudio.Wave;
using System.IO;
using System.Text;
using System.Windows;
using System.Speech.Recognition;
using System.Linq;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace Friday
{
    public class VoiceService
    {
        private readonly List<string> _stopWords = new List<string> { "стоп", "хватит", "довольно", "заткнись", "закрой рот", "отмена" };

        private bool _isSpeaking = false;
        private bool _isWaitingForServer = false;
        private bool _isRecordingCommand = false;
        private bool _isMutedByStopWord = false;
        private string _currentCommandMsgId = null;

        private DateTime _lastSpeechTime = DateTime.MinValue;
        private DateTime _ignoreCommandsUntil = DateTime.MinValue;

        private const float VAD_THRESHOLD = 0.015f;

        private WaveOutEvent _waveOut;
        private WaveInEvent _waveIn;

        private SpeechRecognitionEngine _sapiRecognizer;

        private SettingManager _settingManager;
        private readonly ChangeVoiceService _changeVoiceService;
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

        public VoiceService(SettingManager settingManager, MainWindow mainWindow)
        {
            _settingManager = settingManager;
            _changeVoiceService = new ChangeVoiceService(settingManager);
            _mainWindow = mainWindow;

            ListeningState = new ListeningState();
            musicService = new MusicService();
            musicService.Init();
        }

        private void Sapi_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            if (e.Result == null || string.IsNullOrEmpty(e.Result.Text)) return;
            string text = e.Result.Text.ToLower();

            // Детекция стоп-слов (чтобы перебить бота)
            if (_isSpeaking && _stopWords.Any(w => text.Contains(w)))
            {
                StopSpeaking();
            }
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

                if (_sapiRecognizer != null)
                {
                    try { _sapiRecognizer.RecognizeAsync(RecognizeMode.Multiple); } catch { }
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
                        if (_isSpeaking || _isWaitingForServer) return;
                        if (DateTime.Now < _ignoreCommandsUntil) return;

                        float sum = 0;
                        int sampleCount = e.BytesRecorded / 2;
                        for (int i = 0; i < e.BytesRecorded; i += 2)
                        {
                            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                            float s = sample / 32768f;
                            sum += Math.Abs(s);
                        }
                        float volume = sampleCount > 0 ? sum / sampleCount : 0;

                        if (volume > VAD_THRESHOLD)
                        {
                            _lastSpeechTime = DateTime.Now;
                        }

                        bool isSilence = (DateTime.Now - _lastSpeechTime).TotalMilliseconds > 1500;

                        if (_isRecordingCommand)
                        {
                            byte[] chunk = new byte[e.BytesRecorded];
                            Array.Copy(e.Buffer, chunk, e.BytesRecorded);
                            SendStreamChunk(_currentCommandMsgId, chunk);

                            if (isSilence)
                            {
                                _isRecordingCommand = false;
                                _isWaitingForServer = true;
                                SendStreamEnd(_currentCommandMsgId);
                                _currentCommandMsgId = null;
                            }
                        }
                        else
                        {
                            _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);

                            if (volume <= VAD_THRESHOLD)
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

            var message = new
            {
                type = "голосовое сообщение",
                command = "",
                stream_audio = true,
                audio_base64 = Convert.ToBase64String(initialAudio),
                mac = App.GetMacAddress(),
                timestamp = DateTime.Now,
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
            var message = new { type = "audio_stream_chunk", ui_msg_id = uiMsgId, audio_base64 = Convert.ToBase64String(pcmData) };
            ((App)Application.Current).SendWebSocketMessage(message);
        }

        public void SendStreamEnd(string uiMsgId)
        {
            _mainWindow.Dispatcher.Invoke(() => {
                var msg = _mainWindow.ChatMessages.FirstOrDefault(m => m.UiMsgId == uiMsgId);
                if (msg != null && msg.Text == "🎤 [Слушаю...]") { msg.Text = "⏳ Транскрибирую..."; msg.DisplayText = "⏳ Транскрибирую..."; }
            });

            var message = new { type = "audio_stream_end", ui_msg_id = uiMsgId };
            ((App)Application.Current).SendWebSocketMessage(message);
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
                if (_sapiRecognizer != null) { try { _sapiRecognizer.RecognizeAsyncCancel(); } catch { } }

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
}