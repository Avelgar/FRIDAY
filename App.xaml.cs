using Friday.Services;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Friday
{
    public partial class App : Application
    {
        public string _last_answer = "";
        private ClientWebSocket _webSocket;
        private RegistrationWindow _registrationWindow;
        private MainWindow _mainWindow;
        public VoiceService VoiceService { get; set; }
        private DeviceData _deviceData;
        private System.Timers.Timer _keepAliveTimer;

        public event Action<string> OnMessageReceived;

        private int _openWindowsCount = 0;
        private bool _isConnectionActive = false;

        private bool _isReconnecting = false;
        private readonly object _reconnectLock = new object();
        private readonly Queue<object> _commandQueue = new Queue<object>();
        private readonly object _queueLock = new object();

        public List<string> InstalledApplications { get; private set; }
        private CancellationTokenSource _cancellationTokenSource;
        private static Mutex _mutex;

        private const string appName = "FridayAssistantApp";

        public bool IsWaitingForServerResponse { get; private set; } = false; // Этот флаг больше не нужен
        private CancellationTokenSource _responseTimeoutCts; // И это тоже не нужно

        protected override void OnStartup(StartupEventArgs e)
        {
            SettingManager settingManager = new SettingManager();
            settingManager.LoadSettings();
            AppServices.Init();
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Приложение уже запущено!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                Current.Shutdown();
                return;
            }
            base.OnStartup(e);

            string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\devisedata.json"));

            _cancellationTokenSource = new CancellationTokenSource();

            InitializeWebSocket();

            LoadDeviceData(filePath);

            _keepAliveTimer = new System.Timers.Timer(15000);
            _keepAliveTimer.Elapsed += async (sender, ev) => await CheckConnectionAndSendPingAsync();
            _keepAliveTimer.AutoReset = true;
            _keepAliveTimer.Enabled = true;

            _openWindowsCount++;
        }

        private void ShowAppNotification(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_mainWindow != null && _mainWindow.IsVisible)
                {
                    _mainWindow.ShowSystemMessage(message);
                }
                else
                {
                    MessageBox.Show(message, "Системное уведомление", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });
        }

        public async void SendWebSocketMessage(object data)
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    await SendDataInternal(data);
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка отправки: {ex.Message}");
                }
            }

            lock (_queueLock)
            {
                _commandQueue.Enqueue(data);
            }
        }

        private async Task SendDataInternal(object data)
        {
            if (_webSocket?.State != WebSocketState.Open) return;

            try
            {
                string jsonData = JsonConvert.SerializeObject(data);
                string encodedData = EncodeToBase64(jsonData);
                byte[] buffer = Encoding.UTF8.GetBytes(encodedData);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки: {ex.Message}");
                throw;
            }
        }

        private string EncodeToBase64(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        private string DecodeFromBase64(string base64EncodedData)
        {
            try
            {
                var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
                return Encoding.UTF8.GetString(base64EncodedBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка декодирования Base64: {ex.Message}");
                return string.Empty;
            }
        }

        private async void InitializeWebSocket()
        {
            try
            {
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri("wss://friday-assistant.ru/ws"), _cancellationTokenSource.Token);
                _isConnectionActive = true;

                if (_deviceData != null)
                {
                    var registrationData = new
                    {
                        MAC = GetMacAddress(),
                        DeviceName = _deviceData.DeviceName,
                        Password = _deviceData.Password
                    };
                    await SendDataInternal(registrationData);
                }

                _ = Task.Run(async () => await ReceiveMessages(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                ShowAppNotification($"Ошибка подключения к серверу: {ex.Message}");
            }
        }

        private async Task ReceiveMessages(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                                _isConnectionActive = false;
                                VoiceService.StopSpeaking(); // Останавливаем голос бота при дисконнекте
                                return;
                            }

                            ms.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        ms.Seek(0, SeekOrigin.Begin);
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            using (var reader = new StreamReader(ms, Encoding.UTF8))
                            {
                                string message = await reader.ReadToEndAsync();
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    OnWebSocketMessage(message);
                                });
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка получения сообщения: {ex.Message}");
                    VoiceService.StopSpeaking(); // Останавливаем голос бота при ошибке
                    break;
                }
            }
        }

        private void LoadDeviceData(string filePath)
        {
            if (!File.Exists(filePath))
            {
                OpenRegistrationWindow();
            }
            else
            {
                try
                {
                    var fileContent = File.ReadAllText(filePath);
                    _deviceData = JsonConvert.DeserializeObject<DeviceData>(fileContent);

                    if (_deviceData == null || string.IsNullOrEmpty(_deviceData.DeviceName) || string.IsNullOrEmpty(_deviceData.Password))
                    {
                        OpenRegistrationWindow();
                    }
                    else
                    {
                        var registrationData = new
                        {
                            MAC = GetMacAddress(),
                            DeviceName = _deviceData.DeviceName,
                            Password = _deviceData.Password
                        };
                        SendWebSocketMessage(registrationData);
                    }
                }
                catch (JsonException)
                {
                    OpenRegistrationWindow();
                }
            }
        }

        private void OnWebSocketMessage(string message)
        {
            try
            {
                string answer = DecodeFromBase64(message);
                if (string.IsNullOrEmpty(answer)) return;

                if (answer.Contains("connection_timeout"))
                {
                    _isConnectionActive = false;
                    ShowAppNotification("Превышено время ожидания соединения сервером.");
                    VoiceService.StopSpeaking();
                    return;
                }

                if (answer.Contains("Это имя устройства уже занято. Пожалуйста, выберите другое."))
                {
                    ShowAppNotification("Это имя устройства уже занято. Пожалуйста, выберите другое.");
                    if (_registrationWindow == null)
                    {
                        OpenRegistrationWindow();
                    }
                }
                else if (answer.Contains("Данные успешно обработаны!"))
                {
                    try
                    {
                        bool mainWindowExists = _mainWindow != null && _mainWindow.IsVisible;
                        var response = JsonConvert.DeserializeObject<dynamic>(answer);

                        if (_registrationWindow != null && _registrationWindow.IsVisible)
                        {
                            string deviceName = _registrationWindow.DeviceName;
                            string password = _registrationWindow.Password;
                            UpdateDeviceDataFile(deviceName, password);

                            OpenMainWindow(response);
                            _registrationWindow.Close();
                            _registrationWindow = null;
                        }
                        else if (!mainWindowExists)
                        {
                            OpenMainWindow(response);
                        }
                        else
                        {
                            _mainWindow.UpdateData(response);
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowAppNotification($"Критическая ошибка при запуске интерфейса: {ex.Message}");
                    }
                }
                // 1. ПОТОКОВЫЙ ЗВУК ОТ БОТА
                else if (answer.Contains("\"type\":\"audio_chunk\"") || answer.Contains("\"type\": \"audio_chunk\""))
                {
                    var chunk = JsonConvert.DeserializeObject<AudioChunkMessage>(answer);
                    if (VoiceService != null) VoiceService.AppendAudioChunk(chunk.AudioBase64);
                    return;
                }
                // 2. ПОЛУЧЕНА ТРАНСКРИПЦИЯ ГОЛОСА ПОЛЬЗОВАТЕЛЯ
                else if (answer.Contains("\"type\":\"user_transcription\"") || answer.Contains("\"type\": \"user_transcription\""))
                {
                    try
                    {
                        var trans = JsonConvert.DeserializeObject<UserTranscriptionMessage>(answer);
                        Application.Current.Dispatcher.Invoke(() => {
                            var msg = _mainWindow?.ChatMessages.LastOrDefault(m => m.IsUser && m.Id == trans.UiMsgId);
                            if (msg != null)
                            {
                                msg.Text = trans.Text;
                                msg.DisplayText = trans.Text;
                            }
                        });
                    }
                    catch (Exception ex) { Console.WriteLine($"Ошибка парсинга STT: {ex.Message}"); }
                    return;
                }
                // 3. УДАЛЕНИЕ СООБЩЕНИЯ (ЕСЛИ БОТ УПАЛ ИЛИ НЕ ОТВЕТИЛ)
                else if (answer.Contains("\"type\":\"delete_message\"") || answer.Contains("\"type\": \"delete_message\""))
                {
                    try
                    {
                        var delData = JsonConvert.DeserializeObject<dynamic>(answer);
                        string uiMsgId = delData.ui_msg_id?.ToString();
                        Application.Current.Dispatcher.Invoke(() => {
                            var msg = _mainWindow?.ChatMessages.FirstOrDefault(m => m.Id == uiMsgId);
                            if (msg != null) _mainWindow?.ChatMessages.Remove(msg);
                        });
                    }
                    catch (Exception ex) { Console.WriteLine($"Ошибка удаления сообщения: {ex.Message}"); }
                    return;
                }
                // 4. ЕДИНЫЙ ВХОД ДЛЯ ВСЕХ СООБЩЕНИЙ И КОМАНД БОТА
                else if (answer.Contains("new_message"))
                {
                    try
                    {
                        _last_answer = answer;
                        var cmdResp = JsonConvert.DeserializeObject<CommandResponse>(answer);

                        if (cmdResp != null)
                        {
                            if (VoiceService != null) VoiceService.ResetWaitingForServer();

                            Application.Current.Dispatcher.Invoke(() => {
                                var pendingMsg = _mainWindow?.ChatMessages.LastOrDefault(m => m.IsUser && m.Id == cmdResp.UiMsgId);
                                if (pendingMsg != null)
                                {
                                    pendingMsg.Id = cmdResp.UserMsgId?.ToString() ?? Guid.NewGuid().ToString();
                                    if (pendingMsg.Text == "⏳ Распознавание...")
                                    {
                                        pendingMsg.Text = "🎤 [Голосовое сообщение]";
                                        pendingMsg.DisplayText = "🎤 [Голосовое сообщение]";
                                    }
                                }

                                if (!string.IsNullOrEmpty(cmdResp.Text))
                                {
                                    var existingMsg = _mainWindow?.ChatMessages.FirstOrDefault(m => !m.IsUser && m.Id == cmdResp.MessageId?.ToString());
                                    if (existingMsg != null)
                                    {
                                        existingMsg.Text += cmdResp.Text;
                                        existingMsg.DisplayText = existingMsg.Text;
                                    }
                                    else
                                    {
                                        VoiceService?.ShowTextInChat(cmdResp.Sender ?? "Бот", cmdResp.Text, cmdResp.MessageId?.ToString());
                                    }
                                }
                            });

                            if (!string.IsNullOrEmpty(cmdResp.AudioBase64))
                            {
                                Task.Run(() => VoiceService?.PlayNativeAudio(cmdResp.AudioBase64));
                            }

                            if (cmdResp.Actions != null && VoiceService != null)
                            {
                                bool needsDataResponse = false;
                                bool needProcesses = false;
                                bool needPrograms = false;

                                foreach (var action in cmdResp.Actions)
                                {
                                    if (action.ActionType?.ToLower() == "get_running_processes" || action.ActionType?.ToLower() == "get_installed_programs")
                                    {
                                        needsDataResponse = true;
                                        if (action.ActionType?.ToLower() == "get_running_processes") needProcesses = true;
                                        if (action.ActionType?.ToLower() == "get_installed_programs") needPrograms = true;
                                    }
                                    else
                                    {
                                        var actionItem = new VoiceService.Actions
                                        {
                                            ActionType = action.ActionType?.Trim(),
                                            ActionText = action.ActionValue?.Trim(),
                                            Sender = cmdResp.Sender,
                                            MessageId = cmdResp.MessageId?.ToString(),
                                            UserMsgId = cmdResp.UserMsgId?.ToString(),
                                            IsLocal = false,
                                            AudioBase64 = cmdResp.AudioBase64
                                        };
                                        if (VoiceService != null) _ = VoiceService.ProcessAction(actionItem);
                                    }
                                }

                                if (needsDataResponse)
                                {
                                    string processOutput = "";
                                    if (needPrograms) InstalledApplications = GetInstalledApplications();
                                    if (needProcesses)
                                    {
                                        var userApps = System.Diagnostics.Process.GetProcesses()
                                            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                                            .Select(p => $"{p.ProcessName} (ID: {p.Id})").ToList();
                                        processOutput = string.Join(", ", userApps);
                                    }

                                    // ТЕПЕРЬ ПЕРЕДАЕМ И voice_type ТУДА!
                                    var dataResponse = new
                                    {
                                        command_to_device = cmdResp.OriginalCommand,
                                        processes = processOutput,
                                        programs = InstalledApplications,
                                        source_name = cmdResp.SourceDevice,
                                        user_msg_id = cmdResp.UserMsgId,
                                        voice_type = SettingManager.Setting.VoiceType
                                    };
                                    SendWebSocketMessage(dataResponse);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowAppNotification($"Ошибка обработки сообщения: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Неизвестный пакет: {answer}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки сообщения: {ex.Message}");
            }
        }

        public void UpdateDeviceDataFile(string deviceName, string password)
        {
            string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\devisedata.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            var deviceData = new DeviceData
            {
                DeviceName = deviceName,
                Password = password
            };

            string jsonData = JsonConvert.SerializeObject(deviceData, Formatting.Indented);
            File.WriteAllText(filePath, jsonData, Encoding.UTF8);
        }

        private void OpenRegistrationWindow()
        {
            if (_registrationWindow == null || !_registrationWindow.IsVisible)
            {
                _registrationWindow = new RegistrationWindow();
                _registrationWindow.Closed += (s, ev) => { _registrationWindow = null; };
                _registrationWindow.Show();
            }
        }

        private void OpenMainWindow(dynamic responseData = null)
        {
            _mainWindow = new MainWindow(responseData);
            _mainWindow.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _cancellationTokenSource.Cancel();

            if (_webSocket != null)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Application exit", CancellationToken.None);
                }
                catch { }
                _webSocket.Dispose();
            }

            base.OnExit(e);
        }

        public static string GetMacAddress()
        {
            try
            {
                string registryPath = @"SOFTWARE\Microsoft\Cryptography";
                string developerKey = "MachineGuid";

                using (RegistryKey localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey rgbKey = localKey.OpenSubKey(registryPath))
                    {
                        if (rgbKey != null)
                        {
                            object value = rgbKey.GetValue(developerKey);
                            if (value != null)
                            {
                                string machineGuid = value.ToString().Replace("-", "").ToUpper();

                                using (MD5 md5 = MD5.Create())
                                {
                                    byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(machineGuid));
                                    StringBuilder sb = new StringBuilder();
                                    for (int i = 0; i < 6; i++)
                                    {
                                        sb.Append(hashBytes[i].ToString("X2"));
                                        if (i < 5) sb.Append("-");
                                    }
                                    return sb.ToString();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка получения HWID: {ex.Message}");
            }

            return "00-11-22-33-44-55";
        }

        private async Task CheckConnectionAndSendPingAsync()
        {
            try
            {
                if (_webSocket?.State == WebSocketState.Open)
                {
                    var pingData = new { type = "ping" };
                    await SendDataInternal(pingData);
                    _isConnectionActive = true;
                }
                else
                {
                    _isConnectionActive = false;

                    if (!_isReconnecting)
                    {
                        await ReconnectWebSocket();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке соединения: {ex.Message}");
            }
        }

        public void IncrementWindowCount()
        {
            _openWindowsCount++;
        }

        public void DecrementWindowCount()
        {
            _openWindowsCount--;
            if (_openWindowsCount < 0) _openWindowsCount = 0;
        }

        public List<string> GetInstalledApplications()
        {
            var appList = new List<string>();
            var customApps = Friday.Managers.AppPathManager.LoadApps();

            string tempDir = Path.Combine(Path.GetTempPath(), "FridayAppLaunchers");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            foreach (var app in customApps)
            {
                if (File.Exists(app.Path))
                {
                    string safeName = string.Join("_", app.Name.Split(Path.GetInvalidFileNameChars()));
                    string vbsPath = Path.Combine(tempDir, $"{safeName}.vbs");
                    string script = $"Set WshShell = CreateObject(\"WScript.Shell\")\r\nWshShell.Run Chr(34) & \"{app.Path}\" & Chr(34), 1, False";

                    File.WriteAllText(vbsPath, script, Encoding.UTF8);
                    appList.Add(vbsPath.Replace("\\", "\\\\"));
                }
            }

            return appList;
        }

        private async Task ReconnectWebSocket()
        {
            lock (_reconnectLock)
            {
                if (_webSocket?.State == WebSocketState.Open) return;
                if (_isReconnecting) return;
                _isReconnecting = true;
            }

            try
            {
                await Task.Delay(5000);

                if (_webSocket != null)
                {
                    try
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", CancellationToken.None);
                    }
                    catch { }
                }

                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri("wss://friday-assistant.ru/ws"), _cancellationTokenSource.Token);
                _isConnectionActive = true;

                if (_deviceData != null)
                {
                    var registrationData = new
                    {
                        MAC = GetMacAddress(),
                        DeviceName = _deviceData.DeviceName,
                        Password = _deviceData.Password
                    };
                    await SendDataInternal(registrationData);
                }

                await ProcessCommandQueue();
                _ = Task.Run(async () => await ReceiveMessages(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка переподключения: {ex.Message}");
            }
            finally
            {
                lock (_reconnectLock)
                {
                    _isReconnecting = false;
                }
            }
        }

        private async Task ProcessCommandQueue()
        {
            List<object> commandsToSend;

            lock (_queueLock)
            {
                commandsToSend = new List<object>(_commandQueue);
                _commandQueue.Clear();
            }

            foreach (var command in commandsToSend)
            {
                try
                {
                    await SendDataInternal(command);
                }
                catch
                {
                    lock (_queueLock)
                    {
                        _commandQueue.Enqueue(command);
                    }
                    break;
                }
            }
        }

        // Этот флаг больше не нужен
        // public bool IsWaitingForServerResponse { get; private set; } = false; 
        // private CancellationTokenSource _responseTimeoutCts; 

        public class DataRequest
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("need_processes")]
            public bool NeedProcesses { get; set; }

            [JsonProperty("need_programs")]
            public bool NeedPrograms { get; set; }

            [JsonProperty("need_repeat")]
            public bool NeedRepeat { get; set; }

            [JsonProperty("original_command")]
            public string OriginalCommand { get; set; }

            [JsonProperty("source_device")]
            public string SourceDevice { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("timestamp")]
            public string Timestamp { get; set; }

            [JsonProperty("command_type")]
            public string command_type { get; set; }

            [JsonProperty("user_msg_id")]
            public long? UserMsgId { get; set; }
        }

        public class CommandResponse
        {
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("sender")] public string Sender { get; set; }
            [JsonProperty("text")] public string Text { get; set; }
            [JsonProperty("actions")] public List<DeviceAction> Actions { get; set; }
            [JsonProperty("source_device")] public string SourceDevice { get; set; }
            [JsonProperty("original_command")] public string OriginalCommand { get; set; }
            [JsonProperty("user_msg_id")] public long? UserMsgId { get; set; }
            [JsonProperty("ui_msg_id")] public string UiMsgId { get; set; }
            [JsonProperty("message_id")] public long? MessageId { get; set; }
            [JsonProperty("audio_base64")] public string AudioBase64 { get; set; }
        }

        public class DeviceAction
        {
            [JsonProperty("action_type")]
            public string ActionType { get; set; }

            [JsonProperty("action_value")]
            public string ActionValue { get; set; }
        }

        public class UserTranscriptionMessage
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("user_msg_id")]
            public long? UserMsgId { get; set; }

            [JsonProperty("ui_msg_id")] // <--- ВОТ ЭТОГО НЕ ХВАТАЛО
            public string UiMsgId { get; set; }

            [JsonProperty("text")]
            public string Text { get; set; }
        }

        public class AudioChunkMessage
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("audio_base64")]
            public string AudioBase64 { get; set; }
        }

        public class DeviceData
        {
            public string DeviceName { get; set; }
            public string Password { get; set; }
        }
    }
}