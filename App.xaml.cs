using FigmaToWpf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Speech.Synthesis;
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

        private bool _voicesInstalled = false;

        private bool _isReconnecting = false;
        private readonly object _reconnectLock = new object();
        private readonly Queue<object> _commandQueue = new Queue<object>();
        private readonly object _queueLock = new object();

        public List<string> InstalledApplications { get; private set; }
        private CancellationTokenSource _cancellationTokenSource;
        private static Mutex _mutex;
        protected override void OnStartup(StartupEventArgs e)
        {
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
            CheckAndInstallVoices();

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
                        Application.Current.Dispatcher.Invoke(() => {
                            OnWebSocketMessage(message);
                        });
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                        _isConnectionActive = false;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Operation was cancelled, break the loop
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка получения сообщения: {ex.Message}");
                    break;
                }
            }
        }

        private void CheckAndInstallVoices()
        {
            try
            {
                using (var synth = new SpeechSynthesizer())
                {
                    var installedVoices = synth.GetInstalledVoices()
                        .Where(v => v.Enabled)
                        .Select(v => v.VoiceInfo.Name)
                        .ToList();

                    // Проверяем наличие всех требуемых голосов
                    var requiredVoices = new[] { "Aleksandr", "Anna", "Elena", "Irina" };
                    var missingVoices = requiredVoices.Where(v =>
                        !installedVoices.Any(iv => iv.Contains(v))).ToList();

                    if (!missingVoices.Any())
                    {
                        _voicesInstalled = true;
                        return;
                    }

                    // Показываем сообщение о необходимости установки голосов
                    var result = MessageBox.Show(
                        $"Необходимо установить голоса: {string.Join(", ", missingVoices)}. Установить сейчас?",
                        "Требуется установка голосов",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        InstallVoices(missingVoices);
                        _voicesInstalled = true;
                    }
                    else
                    {
                        _voicesInstalled = false;
                        MessageBox.Show(
                            "Некоторые функции могут работать некорректно без установленных голосов",
                            "Предупреждение",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при проверке голосов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                _voicesInstalled = false;
            }
        }

        private void InstallVoices(List<string> voicesToInstall)
        {
            try
            {
                // Путь к папке с установщиками
                string voicesDir = Path.GetFullPath(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    @"..\..\..\Assets\Voices"));

                foreach (var voice in voicesToInstall)
                {
                    string installerName = $"RHVoice-voice-Russian-{voice}-v4.1.2016.21-setup.exe";
                    string installerPath = Path.Combine(voicesDir, installerName);

                    if (!File.Exists(installerPath))
                    {
                        MessageBox.Show($"Установщик не найден: {installerPath}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }

                    var process = new Process();
                    process.StartInfo.FileName = installerPath;
                    process.StartInfo.Arguments = "/SILENT"; // Тихая установка
                    process.StartInfo.Verb = "runas"; // Запрос прав администратора
                    process.StartInfo.UseShellExecute = true;
                    process.Start();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        MessageBox.Show($"Ошибка при установке голоса {voice}. Код ошибки: {process.ExitCode}",
                            "Ошибка установки", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при установке голосов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                //OnMessageReceived?.Invoke(answer);
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

        // Изменяем модификатор доступа с private на public
        public void UpdateDeviceDataFile(string deviceName, string password)
        {
            string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\devisedata.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            var deviceData = new DeviceData
            {
                DeviceName = deviceName,
                Password = password
            };

            // Сериализация данных в JSON
            string jsonData = JsonConvert.SerializeObject(deviceData, Formatting.Indented);

            // Запись данных в файл (очистка файла, если он существует)
            File.WriteAllText(filePath, jsonData, Encoding.UTF8);
        }

        private void OpenRegistrationWindow()
        {
            // Проверяем, открыто ли окно регистрации
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

        // Не забудьте закрыть WebSocket при завершении приложения
        protected override async void OnExit(ExitEventArgs e)
        {
            _cancellationTokenSource.Cancel();

            if (_webSocket != null)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Application exit", CancellationToken.None);
                }
                catch
                {
                    // Ignore close errors during exit
                }
                _webSocket.Dispose();
            }

            base.OnExit(e);
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
            var programsWithPaths = new List<string>
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files\Microsoft Office\root\Office16\excel.exe",
                @"C:\Program Files\Microsoft Office\root\Office16\powerpnt.exe",
                @"D:\Program Files(x86)\Steam\steamapps\common\dota 2 beta\game\bin\win64\dota2.exe"
            };
            // Экранирование обратных слешей
            foreach (var path in programsWithPaths)
            {
                if (File.Exists(path))
                    appList.Add(path.Replace("\\", "\\\\")); // Двойное экранирование
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
                    catch
                    {
                        // Ignore close errors during reconnect
                    }
                }

                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri("wss://friday-assistant.ru/ws"), _cancellationTokenSource.Token);
                _isConnectionActive = true;

                // Повторно отправляем регистрационные данные
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

                // Отправляем все команды из очереди
                await ProcessCommandQueue();

                // Перезапускаем задачу прослушивания
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