using Friday.Services;
using Newtonsoft.Json;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Text;
using System.Windows;

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

        public bool IsWaitingForServerResponse { get; private set; } = false;
        private CancellationTokenSource _responseTimeoutCts;

        protected override void OnStartup(StartupEventArgs e)
        {
            SettingManager settingManager = new SettingManager();
            settingManager.LoadSettings();
            AppServices.Init();
            const string appName = "FridayAssistantApp";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Приложение уже запущено!");
                Current.Shutdown();
                return;
            }
            base.OnStartup(e);

            // Удалена проверка и установка RHVoice

            string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\devisedata.json"));

            _cancellationTokenSource = new CancellationTokenSource();

            // Инициализация WebSocket
            InitializeWebSocket();

            LoadDeviceData(filePath);

            // Инициализация таймера пинга
            _keepAliveTimer = new System.Timers.Timer(15000);
            _keepAliveTimer.Elapsed += async (sender, e) => await CheckConnectionAndSendPingAsync();
            _keepAliveTimer.AutoReset = true;
            _keepAliveTimer.Enabled = true;

            _openWindowsCount++;
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

            if (!_isReconnecting)
            {
                //await Task.Run(() => ReconnectWebSocket());
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
            catch
            {
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

                // После подключения отправляем регистрационные данные, если они есть
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

                // Запускаем задачу для прослушивания сообщений
                _ = Task.Run(async () => await ReceiveMessages(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения: {ex.Message}");
            }
        }

        private async Task ReceiveMessages(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            OnWebSocketMessage(message);
                        });
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                        _isConnectionActive = false;
                        ClearWaitingForServer();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка получения сообщения: {ex.Message}");
                    ClearWaitingForServer();
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

        // Обработка всех сообщений от WebSocket
        private void OnWebSocketMessage(string message)
        {
            try
            {
                string answer = DecodeFromBase64(message);
                if (!answer.Contains("data_request") && !answer.Contains("ping"))
                {
                    ClearWaitingForServer();
                }

                if (answer.Contains("connection_timeout"))
                {
                    _isConnectionActive = false;
                    MessageBox.Show(answer.ToString(), "Ошибка соединения", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (answer.Contains("Это имя устройства уже занято. Пожалуйста, выберите другое."))
                {
                    MessageBox.Show("Это имя устройства уже занято. Пожалуйста, выберите другое.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        Console.WriteLine($"Ошибка обработки ответа: {ex.Message}");
                    }
                }
                else if (answer.Contains("new_message"))
                {
                    try
                    {
                        _last_answer = answer;
                        var command_response = JsonConvert.DeserializeObject<CommandResponse>(answer);

                        foreach (var action in command_response.Actions)
                        {
                            var actionParts = action.Split(new[] { '|' }, 2);
                            if (actionParts.Length == 2)
                            {
                                var actionItem = new VoiceService.Actions
                                {
                                    ActionType = actionParts[0].Trim(),
                                    ActionText = actionParts[1].Trim(),
                                    Sender = command_response.Sender
                                };
                                _ = VoiceService.ProcessAction(actionItem);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка обработки actions: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else if (answer.Contains("data_request"))
                {
                    try
                    {
                        string processOutput = "";
                        var request = JsonConvert.DeserializeObject<DataRequest>(answer);

                        if (request.NeedPrograms)
                        {
                            InstalledApplications = GetInstalledApplications();
                        }

                        if (request.NeedProcesses)
                        {
                            var userApps = System.Diagnostics.Process.GetProcesses()
                                .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                                .Select(p => $"{p.ProcessName} (ID: {p.Id})")
                                .ToList();

                            processOutput = string.Join(", ", userApps);
                        }

                        var new_response = new
                        {
                            command_to_device = request.OriginalCommand,
                            processes = processOutput,
                            source_name = request.SourceDevice,
                            name = request.Name,
                            programs = InstalledApplications,
                            command_type = request.command_type
                        };

                        SendWebSocketMessage(new_response);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка: {ex.Message}");
                    }
                }
                else
                {
                    MessageBox.Show(answer);
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
                _registrationWindow.Closed += (s, e) => { _registrationWindow = null; };
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
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (NetworkInterface networkInterface in networkInterfaces)
            {
                if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                    !string.IsNullOrEmpty(networkInterface.GetPhysicalAddress().ToString()))
                {
                    string macAddress = networkInterface.GetPhysicalAddress().ToString();
                    if (macAddress.Length == 12)
                    {
                        return string.Join("-", Enumerable.Range(0, 6)
                            .Select(i => macAddress.Substring(i * 2, 2)));
                    }
                    return macAddress;
                }
            }

            return string.Empty;
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

                    File.WriteAllText(vbsPath, script, System.Text.Encoding.UTF8);
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

                Console.WriteLine("Попытка переподключения WebSocket...");

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

        public void MarkAsWaitingForServer()
        {
            IsWaitingForServerResponse = true;
            _responseTimeoutCts?.Cancel();
            _responseTimeoutCts = new CancellationTokenSource();

            Task.Delay(15000, _responseTimeoutCts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled && IsWaitingForServerResponse)
                    IsWaitingForServerResponse = false;
            });
        }

        public void ClearWaitingForServer()
        {
            IsWaitingForServerResponse = false;
            _responseTimeoutCts?.Cancel();
        }


        public class DataRequest
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("need_processes")]
            public bool NeedProcesses { get; set; }

            [JsonProperty("need_programs")]
            public bool NeedPrograms { get; set; }

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
        }

        public class CommandResponse
        {
            public string Type { get; set; }
            public string Sender { get; set; }
            public List<string> Actions { get; set; }
            public string SourceDevice { get; set; }
            public string Timestamp { get; set; }
        }

        public class DeviceData
        {
            public string DeviceName { get; set; }
            public string Password { get; set; }
        }
    }
}