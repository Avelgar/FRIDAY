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
        private RegistrationWindow _registrationWindow; // Окно ЖЕЛЕЗА
        private MainWindow _mainWindow;
        public VoiceService VoiceService { get; set; }
        private DeviceData _deviceData;
        private System.Timers.Timer _keepAliveTimer;

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
        private string _ignoredMessageId = null;

        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        // ДАННЫЕ АККАУНТА
        public class AccountData { public string Login { get; set; } public string Token { get; set; } }
        private AccountData _accountData;
        private bool _isMainWindowOpened = false;

        /// <summary>
        /// JWT-токен текущего аккаунта или null в гостевом режиме.
        /// Нужен для REST-эндпоинтов диалогов (/api/get_dialogs и др.),
        /// которые авторизуются по токену, а НЕ по MAC.
        /// </summary>
        public string AccountToken => _accountData?.Token;

        /// <summary>Логин текущего аккаунта или null в гостевом режиме.</summary>
        public string AccountLogin => _accountData?.Login;

        /// <summary>true, если пользователь авторизован (не гость).</summary>
        public bool IsLoggedIn => !string.IsNullOrEmpty(_accountData?.Token);

        protected override void OnStartup(StartupEventArgs e)
        {
            SettingManager settingManager = new SettingManager();
            settingManager.LoadSettings();
            AppServices.Init();

            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            bool createdNew;
            _mutex = new Mutex(true, appName, out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Приложение уже запущено!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                Current.Shutdown();
                return;
            }
            base.OnStartup(e);

            _cancellationTokenSource = new CancellationTokenSource();
            InitializeWebSocket();

            // 1. ЗАГРУЖАЕМ ЖЕЛЕЗО (Гостевой режим)
            string deviceFilePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\devisedata.json"));
            LoadDeviceData(deviceFilePath);

            // 2. ПОДГРУЖАЕМ АККАУНТ (Но пока не синхронизируем, ждем ответа от сервера по железу)
            string accountFilePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\account.json"));
            if (File.Exists(accountFilePath))
            {
                try { _accountData = JsonConvert.DeserializeObject<AccountData>(File.ReadAllText(accountFilePath)); }
                catch { }
            }

            _keepAliveTimer = new System.Timers.Timer(15000);
            _keepAliveTimer.Elapsed += async (sender, ev) => await CheckConnectionAndSendPingAsync();
            _keepAliveTimer.AutoReset = true;
            _keepAliveTimer.Enabled = true;

            _openWindowsCount++;
        }

        // --- МЕТОДЫ ЖЕЛЕЗА ---
        private void LoadDeviceData(string filePath)
        {
            if (!File.Exists(filePath)) OpenRegistrationWindow();
            else
            {
                try
                {
                    var fileContent = File.ReadAllText(filePath);
                    _deviceData = JsonConvert.DeserializeObject<DeviceData>(fileContent);
                    if (_deviceData == null || string.IsNullOrEmpty(_deviceData.DeviceName) || string.IsNullOrEmpty(_deviceData.Password))
                        OpenRegistrationWindow();
                    else
                        SendWebSocketMessage(new { MAC = GetMacAddress(), DeviceName = _deviceData.DeviceName, Password = _deviceData.Password });
                }
                catch (JsonException) { OpenRegistrationWindow(); }
            }
        }

        public void UpdateDeviceDataFile(string deviceName, string password)
        {
            string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\devisedata.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            _deviceData = new DeviceData { DeviceName = deviceName, Password = password };
            File.WriteAllText(filePath, JsonConvert.SerializeObject(_deviceData, Formatting.Indented), Encoding.UTF8);
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

        // --- МЕТОДЫ АККАУНТА ---
        public void UpdateAccountData(string login, string token)
        {
            string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\account.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            _accountData = new AccountData { Login = login, Token = token };
            File.WriteAllText(filePath, JsonConvert.SerializeObject(_accountData, Formatting.Indented), Encoding.UTF8);

            CheckAccountSync();
        }

        public void CheckAccountSync()
        {
            if (_accountData != null && !string.IsNullOrEmpty(_accountData.Token))
            {
                var syncData = new
                {
                    type = "account_sync",
                    token = _accountData.Token,
                    mac = GetMacAddress()
                };
                SendWebSocketMessage(syncData);
            }
        }
        public void ResetAccountData()
        {
            _accountData = null;
        }

        private void OpenMainWindow(dynamic responseData = null)
        {
            _mainWindow = new MainWindow(responseData);
            _mainWindow.Show();
        }

        private void ShowAppNotification(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_mainWindow != null && _mainWindow.IsVisible) _mainWindow.ShowSystemMessage(message);
                else MessageBox.Show(message, "Системное уведомление", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        // --- WEBSOCKET ЛОГИКА ---
        public async void SendWebSocketMessage(object data)
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                try { await SendDataInternal(data); return; }
                catch (Exception ex) { Console.WriteLine($"Ошибка отправки: {ex.Message}"); }
            }
            lock (_queueLock) { _commandQueue.Enqueue(data); }
        }

        private async Task SendDataInternal(object data)
        {
            if (_webSocket?.State != WebSocketState.Open) return;

            string jsonData = JsonConvert.SerializeObject(data);
            string encodedData = EncodeToBase64(jsonData);
            byte[] buffer = Encoding.UTF8.GetBytes(encodedData);

            await _sendLock.WaitAsync();
            try
            {
                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
                }
            }
            catch (Exception ex) { Console.WriteLine($"Ошибка отправки: {ex.Message}"); throw; }
            finally { _sendLock.Release(); }
        }

        private string EncodeToBase64(string plainText) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        private string DecodeFromBase64(string base64EncodedData)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(base64EncodedData)); }
            catch (Exception ex) { Console.WriteLine($"Критическая ошибка декодирования: {ex.Message}"); return string.Empty; }
        }

        private async void InitializeWebSocket()
        {
            try
            {
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri("wss://friday-assistant.ru/ws"), _cancellationTokenSource.Token);
                _isConnectionActive = true;

                // ФИКС ЗДЕСЬ: Выталкиваем из очереди сообщения, которые скопились, пока мы подключались!
                await ProcessCommandQueue();

                _ = Task.Run(async () => await ReceiveMessages(_cancellationTokenSource.Token));
            }
            catch (Exception ex) { ShowAppNotification($"Ошибка подключения к серверу: {ex.Message}"); }
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
                                VoiceService?.StopSpeaking();
                                return;
                            }
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        ms.Seek(0, SeekOrigin.Begin);
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            using (var reader = new StreamReader(ms, Encoding.UTF8))
                            {
                                string message = await reader.ReadToEndAsync();
                                Application.Current.Dispatcher.Invoke(() => { OnWebSocketMessage(message); });
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Console.WriteLine($"Ошибка получения сообщения: {ex.Message}"); VoiceService?.StopSpeaking(); break; }
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
                    Application.Current.Dispatcher.Invoke(() => { _mainWindow?.ShowSystemMessage("Превышено время ожидания сервером."); });
                    VoiceService?.StopSpeaking();
                    return;
                }

                if (answer.Contains("Недействительный токен"))
                {
                    Application.Current.Dispatcher.Invoke(() => { _mainWindow?.ShowSystemMessage("Токен устарел. Выполните вход в аккаунт заново."); });
                    // Сбрасываем токен, но не закрываем программу, так как Гостевой режим работает!
                    _accountData = null;
                    string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\account.json"));
                    if (File.Exists(filePath)) File.Delete(filePath);
                    // Токен протух -> уходим в гостевой режим: прячем панель диалогов,
                    // возвращаем кнопку "Очистить".
                    Application.Current.Dispatcher.Invoke(() => _mainWindow?.ApplyGuestMode());
                    return;
                }

                if (answer.Contains("Это имя устройства уже занято"))
                {
                    Application.Current.Dispatcher.Invoke(() => { _mainWindow?.ShowSystemMessage("Имя устройства занято."); });
                    if (_registrationWindow == null) OpenRegistrationWindow();
                    return;
                }

                // 1. УСПЕШНАЯ СИНХРОНИЗАЦИЯ АККАУНТА (Пришла глобальная история)
                if (answer.Contains("\"type\":\"account_sync_success\"") || answer.Contains("\"type\": \"account_sync_success\""))
                {
                    var response = JsonConvert.DeserializeObject<dynamic>(answer);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_mainWindow != null)
                        {
                            // ВЫЗЫВАЕМ НАШ НОВЫЙ МЕТОД: Обновляем интерфейс БЕЗ пересоздания окна!
                            _mainWindow.SyncAccountData(response);
                            _mainWindow.ShowSystemMessage("Аккаунт синхронизирован!");
                        }
                    });
                    return;
                }

                // 2. УСПЕШНАЯ РЕГИСТРАЦИЯ ЖЕЛЕЗА (Пришла локальная история)
                if (answer.Contains("Данные успешно обработаны!"))
                {
                    var response = JsonConvert.DeserializeObject<dynamic>(answer);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Если открыто окно регистрации железа - сохраняем данные и закрываем его
                        if (_registrationWindow != null && _registrationWindow.IsVisible)
                        {
                            UpdateDeviceDataFile(_registrationWindow.DeviceName, _registrationWindow.Password);
                            _registrationWindow.Close();
                            _registrationWindow = null;
                        }

                        if (!_isMainWindowOpened)
                        {
                            OpenMainWindow(response);
                            _isMainWindowOpened = true;
                            // Как только железо зарегалось, проверяем, есть ли аккаунт
                            CheckAccountSync();
                        }
                        else
                        {
                            _mainWindow.UpdateData(response);
                        }
                    });
                    return;
                }

                // === СОЗДАН НОВЫЙ ДИАЛОГ (сервер сам завёл его при dialog_id: null) ===
                if (answer.Contains("\"type\":\"dialog_created\"") || answer.Contains("\"type\": \"dialog_created\""))
                {
                    try
                    {
                        var d = JsonConvert.DeserializeObject<dynamic>(answer);
                        long newId = (long)d.dialog_id;
                        string newName = d.name?.ToString() ?? "Новый диалог";
                        Application.Current.Dispatcher.Invoke(() => _mainWindow?.OnDialogCreated(newId, newName));
                    }
                    catch (Exception ex) { Console.WriteLine($"Ошибка dialog_created: {ex.Message}"); }
                    return;
                }

                // === ИИ ПРИДУМАЛ ИМЯ ДИАЛОГУ (action "название диалога" отработал на сервере) ===
                if (answer.Contains("\"type\":\"dialog_renamed\"") || answer.Contains("\"type\": \"dialog_renamed\""))
                {
                    try
                    {
                        var d = JsonConvert.DeserializeObject<dynamic>(answer);
                        long renId = (long)d.dialog_id;
                        string renName = d.name?.ToString() ?? "";
                        Application.Current.Dispatcher.Invoke(() => _mainWindow?.OnDialogRenamed(renId, renName));
                    }
                    catch (Exception ex) { Console.WriteLine($"Ошибка dialog_renamed: {ex.Message}"); }
                    return;
                }

                if (answer.Contains("\"type\":\"msg_id_map\"") || answer.Contains("\"type\": \"msg_id_map\""))
                {
                    try
                    {
                        var mapData = JsonConvert.DeserializeObject<dynamic>(answer);
                        string uiMsgId = mapData.ui_msg_id?.ToString();
                        string realMsgId = mapData.user_msg_id?.ToString();
                        if (!string.IsNullOrEmpty(uiMsgId) && !string.IsNullOrEmpty(realMsgId))
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var msg = _mainWindow?.ChatMessages.LastOrDefault(m => m.UiMsgId == uiMsgId);
                                if (msg != null) msg.Id = realMsgId;
                            });
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"Ошибка msg_id_map: {ex.Message}"); }
                    return;
                }

                if (answer.Contains("\"type\":\"audio_chunk\"") || answer.Contains("\"type\": \"audio_chunk\""))
                {
                    var chunk = JsonConvert.DeserializeObject<AudioChunkMessage>(answer);
                    if (VoiceService != null) VoiceService.AppendAudioChunk(chunk.AudioBase64);
                    return;
                }

                if (answer.Contains("\"type\":\"user_transcription\"") || answer.Contains("\"type\": \"user_transcription\""))
                {
                    try
                    {
                        var trans = JsonConvert.DeserializeObject<UserTranscriptionMessage>(answer);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var msg = _mainWindow?.ChatMessages.LastOrDefault(m => m.IsUser && m.UiMsgId == trans.UiMsgId);
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

                if (answer.Contains("\"type\":\"delete_message\"") || answer.Contains("\"type\": \"delete_message\""))
                {
                    try
                    {
                        var delData = JsonConvert.DeserializeObject<dynamic>(answer);
                        string uiMsgId = delData.ui_msg_id?.ToString();
                        Application.Current.Dispatcher.Invoke(() => {
                            var msg = _mainWindow?.ChatMessages.FirstOrDefault(m => m.UiMsgId == uiMsgId);
                            if (msg != null) _mainWindow?.ChatMessages.Remove(msg);
                        });
                        VoiceService?.ServerFinishedResponse();
                    }
                    catch (Exception ex) { Console.WriteLine($"Ошибка удаления сообщения: {ex.Message}"); }
                    return;
                }

                if (answer.Contains("new_message"))
                {
                    try
                    {
                        _last_answer = answer;
                        var cmdResp = JsonConvert.DeserializeObject<CommandResponse>(answer);

                        if (cmdResp != null)
                        {
                            bool isFinalMessage = string.IsNullOrEmpty(cmdResp.Text) && (cmdResp.Actions == null || cmdResp.Actions.Count == 0);
                            if (isFinalMessage)
                            {
                                VoiceService?.ServerFinishedResponse();
                            }

                            string currentMsgId = cmdResp.MessageId?.ToString() ?? cmdResp.UiMsgId;

                            if (cmdResp.Actions != null && VoiceService != null)
                            {
                                bool needsDataResponse = false;
                                bool needProcesses = false;
                                bool needPrograms = false;
                                bool needScreenshot = false; // <--- ДОБАВИЛИ

                                foreach (var action in cmdResp.Actions)
                                {
                                    if (action.ActionType?.ToLower() == "очистка истории")
                                    {
                                        _ignoredMessageId = currentMsgId;
                                    }

                                    // ДОБАВИЛИ request_screenshot В ПРОВЕРКУ
                                    if (action.ActionType?.ToLower() == "get_running_processes" ||
                                        action.ActionType?.ToLower() == "get_installed_programs" ||
                                        action.ActionType?.ToLower() == "request_screenshot")
                                    {
                                        needsDataResponse = true;
                                        if (action.ActionType?.ToLower() == "get_running_processes") needProcesses = true;
                                        if (action.ActionType?.ToLower() == "get_installed_programs") needPrograms = true;
                                        if (action.ActionType?.ToLower() == "request_screenshot") needScreenshot = true;
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
                                    string screenshotB64 = null; // <--- ДОБАВИЛИ
                                    string screenRes = "";       // <--- ДОБАВИЛИ

                                    if (needPrograms) InstalledApplications = GetInstalledApplications();
                                    if (needProcesses)
                                    {
                                        var userApps = System.Diagnostics.Process.GetProcesses()
                                            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                                            .Select(p => $"{p.ProcessName} (ID: {p.Id})").ToList();
                                        processOutput = string.Join(", ", userApps);
                                    }

                                    // === ЗАПРАШИВАЕМ ТИХИЙ СКРИНШОТ ===
                                    if (needScreenshot)
                                    {
                                        var screenData = _mainWindow?.CaptureScreenForAI() ?? (null, 0, 0);
                                        screenshotB64 = screenData.Base64;
                                        screenRes = $"{screenData.Width}x{screenData.Height}";
                                    }

                                    var dataResponse = new
                                    {
                                        command_to_device = cmdResp.OriginalCommand,
                                        processes = processOutput,
                                        programs = InstalledApplications,
                                        screenshot_base64_received = screenshotB64, // <--- ОТПРАВЛЯЕМ ФОТКУ
                                        screen_resolution = screenRes,              // <--- ОТПРАВЛЯЕМ РАЗМЕР ЭКРАНА
                                        source_name = cmdResp.SourceDevice,
                                        user_msg_id = cmdResp.UserMsgId,
                                        voice_type = SettingManager.Setting.VoiceType
                                    };
                                    SendWebSocketMessage(dataResponse);
                                }
                            }
                            Application.Current.Dispatcher.Invoke(() => {
                                var pendingMsg = _mainWindow?.ChatMessages.LastOrDefault(m => m.IsUser && m.UiMsgId == cmdResp.UiMsgId);
                                if (pendingMsg != null)
                                {
                                    pendingMsg.Id = cmdResp.UserMsgId?.ToString() ?? pendingMsg.Id;
                                    if (pendingMsg.Text == "⏳ Транскрибирую...")
                                    {
                                        pendingMsg.Text = "🎤 [Аудиосообщение]";
                                        pendingMsg.DisplayText = "🎤 [Аудиосообщение]";
                                    }
                                }

                                if (!string.IsNullOrEmpty(cmdResp.Text) && currentMsgId != _ignoredMessageId)
                                {
                                    var existingMsg = _mainWindow?.ChatMessages.FirstOrDefault(m => !m.IsUser && (m.Id == cmdResp.MessageId?.ToString() || (m.UiMsgId != null && m.UiMsgId == cmdResp.UiMsgId)));
                                    if (existingMsg != null)
                                    {
                                        existingMsg.Text += cmdResp.Text;
                                        existingMsg.DisplayText = existingMsg.Text;
                                    }
                                    else
                                    {
                                        VoiceService?.ShowTextInChat(cmdResp.Sender ?? "Бот", cmdResp.Text, cmdResp.MessageId?.ToString(), cmdResp.UiMsgId);
                                    }
                                }
                            });

                            if (!string.IsNullOrEmpty(cmdResp.AudioBase64))
                            {
                                Task.Run(() => VoiceService?.PlayNativeAudio(cmdResp.AudioBase64));
                            }
                        }
                    }
                    catch (Exception ex) { ShowAppNotification($"Ошибка обработки сообщения: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Console.WriteLine($"Ошибка обработки сообщения: {ex.Message}"); }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _cancellationTokenSource.Cancel();
            if (_webSocket != null) { try { await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Application exit", CancellationToken.None); } catch { } _webSocket.Dispose(); }
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
            catch (Exception ex) { Console.WriteLine($"Ошибка получения HWID: {ex.Message}"); }
            return "00-11-22-33-44-55";
        }

        private async Task CheckConnectionAndSendPingAsync()
        {
            try
            {
                if (_webSocket?.State == WebSocketState.Open) { await SendDataInternal(new { type = "ping" }); _isConnectionActive = true; }
                else { _isConnectionActive = false; if (!_isReconnecting) await ReconnectWebSocket(); }
            }
            catch (Exception ex) { Console.WriteLine($"Ошибка при проверке соединения: {ex.Message}"); }
        }

        public void IncrementWindowCount() => _openWindowsCount++;
        public void DecrementWindowCount() { _openWindowsCount--; if (_openWindowsCount < 0) _openWindowsCount = 0; }

        public List<string> GetInstalledApplications()
        {
            var appList = new List<string>();
            var customApps = Friday.Managers.AppPathManager.LoadApps();
            foreach (var app in customApps) { if (File.Exists(app.Path)) appList.Add(app.Path); }
            return appList;
        }

        private async Task ReconnectWebSocket()
        {
            lock (_reconnectLock) { if (_webSocket?.State == WebSocketState.Open || _isReconnecting) return; _isReconnecting = true; }
            try
            {
                await Task.Delay(5000);
                if (_webSocket != null) { try { await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", CancellationToken.None); } catch { } }
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri("wss://friday-assistant.ru/ws"), _cancellationTokenSource.Token);
                _isConnectionActive = true;

                if (_deviceData != null)
                    await SendDataInternal(new { MAC = GetMacAddress(), DeviceName = _deviceData.DeviceName, Password = _deviceData.Password });

                await ProcessCommandQueue();
                _ = Task.Run(async () => await ReceiveMessages(_cancellationTokenSource.Token));
            }
            catch (Exception ex) { Console.WriteLine($"Ошибка переподключения: {ex.Message}"); }
            finally { lock (_reconnectLock) { _isReconnecting = false; } }
        }

        private async Task ProcessCommandQueue()
        {
            List<object> commandsToSend;
            lock (_queueLock) { commandsToSend = new List<object>(_commandQueue); _commandQueue.Clear(); }
            foreach (var command in commandsToSend)
            {
                try { await SendDataInternal(command); }
                catch { lock (_queueLock) { _commandQueue.Enqueue(command); } break; }
            }
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

        public class DeviceAction { [JsonProperty("action_type")] public string ActionType { get; set; } [JsonProperty("action_value")] public string ActionValue { get; set; } }

        public class UserTranscriptionMessage
        {
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("user_msg_id")] public long? UserMsgId { get; set; }
            [JsonProperty("ui_msg_id")] public string UiMsgId { get; set; }
            [JsonProperty("text")] public string Text { get; set; }
        }

        public class AudioChunkMessage { [JsonProperty("type")] public string Type { get; set; } [JsonProperty("audio_base64")] public string AudioBase64 { get; set; } }
        public class DeviceData { public string DeviceName { get; set; } public string Password { get; set; } }
    }
}