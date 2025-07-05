using FigmaToWpf;
using Friday;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Speech.Synthesis;
using System.Text;
using System.Windows;
using WebSocketSharp;
using static Friday.VoiceService;

namespace Friday
{
    public partial class App : Application
    {
        private WebSocket _webSocket;
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
        private readonly Queue<object> _commandQueue = new Queue<object>(); // Очередь команд
        private readonly object _queueLock = new object(); // Блокировка для очереди

        public List<string> InstalledApplications { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CheckAndInstallVoices();

            string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\devisedata.json"));


            // Инициализация WebSocket
            InitializeWebSocket();

            LoadDeviceData(filePath);

            // Инициализация таймера пинга
            _keepAliveTimer = new System.Timers.Timer(15000); // 30 секунд
            _keepAliveTimer.Elapsed += async (sender, e) => await CheckConnectionAndSendPingAsync();
            _keepAliveTimer.AutoReset = true;
            _keepAliveTimer.Enabled = true;

            _openWindowsCount++;
        }
        public void SendWebSocketMessage(object data)
        {
            // Если соединение активно - отправляем сразу
            if (_webSocket?.ReadyState == WebSocketState.Open)
            {
                try
                {
                    SendDataInternal(data);
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка отправки: {ex.Message}");
                }
            }

            // Добавляем команду в очередь
            lock (_queueLock)
            {
                _commandQueue.Enqueue(data);
            }

            // Инициируем переподключение, если оно еще не запущено
            if (!_isReconnecting)
            {
                Task.Run(() => ReconnectWebSocket());
            }
        }

        private void SendDataInternal(object data)
        {
            if (_webSocket?.ReadyState != WebSocketState.Open) return;

            try
            {
                string jsonData = JsonConvert.SerializeObject(data);
                string encodedData = EncodeToBase64(jsonData);
                _webSocket.Send(encodedData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки: {ex.Message}");
                throw; // Пробрасываем исключение для обработки выше
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
        private void InitializeWebSocket()
        {
            _webSocket = new WebSocket("ws://blue.fnode.me:8114");
            _webSocket.OnMessage += OnWebSocketMessage;
            _webSocket.OnOpen += (sender, e) =>
            {
                Console.WriteLine("Соединение открыто.");
                _isConnectionActive = true;
            };
            _webSocket.OnClose += (sender, e) =>
            {
                Console.WriteLine("Соединение закрыто.");
                _isConnectionActive = false;
            };
            _webSocket.OnError += (sender, e) =>
            {
                Console.WriteLine($"Ошибка: {e.Message}");
                _isConnectionActive = false;
                Task.Run(() => ReconnectWebSocket()); // Добавить это
            };

            try
            {
                _webSocket.Connect();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при подключении: {ex.Message}");
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
        private void OnWebSocketMessage(object sender, MessageEventArgs e)
        {
            try
            {
                // Декодируем из Base64
                string answer = DecodeFromBase64(e.Data);
                OnMessageReceived?.Invoke(answer);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var responce = JsonConvert.DeserializeObject<dynamic>(answer);

                        // Обработка сообщения о таймауте соединения
                        if (responce?.type == "connection_timeout")
                        {
                            _isConnectionActive = false;
                            MessageBox.Show(responce.message.ToString(), "Ошибка соединения", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        if (answer.Contains("Имя устройства уже занято."))
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
                                var response = JsonConvert.DeserializeObject<dynamic>(answer);
                                // Проверяем, есть ли уже открытое MainWindow
                                bool mainWindowExists = _mainWindow != null && _mainWindow.IsVisible;

                                if (_registrationWindow != null && _registrationWindow.IsVisible)
                                {
                                    // Если есть окно регистрации, открываем MainWindow и закрываем регистрацию
                                    OpenMainWindow(response);
                                    _registrationWindow.Close();
                                    _registrationWindow = null;
                                }
                                else if (!mainWindowExists)
                                {
                                    // Если MainWindow не открыто, открываем его
                                    OpenMainWindow(response);
                                }
                                else
                                {
                                    // Если MainWindow уже открыто, просто обновляем его данные
                                    //_mainWindow.UpdateData(response);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Ошибка обработки ответа: {ex.Message}");
                            }
                        }
                        else if (answer.Contains("actions"))
                        {

                            try
                            {
                                var response = JsonConvert.DeserializeObject<CommandResponse>(answer);
                                bool hasScreenshot = response.Actions.Any(a => a.StartsWith("скриншот|"));
                                List<string> otherActions = response.Actions.Where(a => !a.StartsWith("скриншот|")).ToList();
                                foreach (var action in otherActions)
                                {
                                    var actionParts = action.Split('|');
                                    if (actionParts.Length == 2)
                                    {
                                        var oneAction = new VoiceService.Actions
                                        {
                                            ActionType = actionParts[0].Trim(),
                                            ActionText = actionParts[1].Trim()
                                        };

                                        // Асинхронный вызов без блокировки UI
                                        _ = VoiceService.ProcessAction(oneAction);  // Не используем .Wait()
                                    }
                                }

                                // Затем выполняем скриншот, если он есть
                                if (hasScreenshot)
                                {
                                    var screenshotAction = response.Actions.First(a => a.StartsWith("скриншот|"));
                                    var actionParts = screenshotAction.Split('|');
                                    if (actionParts.Length == 2)
                                    {
                                        var actionItem = new Actions
                                        {
                                            ActionType = actionParts[0].Trim(),
                                            ActionText = actionParts[1].Trim()
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

                                var response = new
                                {
                                    command_to_device = request.OriginalCommand,
                                    processes = processOutput,
                                    source_name = request.SourceDevice,
                                    name = request.Name,
                                    programs = InstalledApplications
                                };

                                SendWebSocketMessage(response);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Ошибка: {ex.Message}");
                            }
                        }
                        else
                        {
                            //MessageBox.Show($"Сообщение от сервера {answer}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch
                    {
                        // Обработка некорректного JSON
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка декодирования сообщения: {ex.Message}");
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
        }
        public class CommandResponse
        {
            public string Type { get; set; }
            public List<string> Actions { get; set; }
            public string SourceDevice { get; set; }
            public string Timestamp { get; set; }
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
                _registrationWindow.Show();
            }
        }

        private void OpenMainWindow(dynamic responseData = null)
        {
            _mainWindow = new MainWindow(responseData);
            _mainWindow.Show();
        }

        // Не забудьте закрыть WebSocket при завершении приложения
        protected override void OnExit(ExitEventArgs e)
        {
            _webSocket?.Close();
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
                if (_webSocket?.ReadyState == WebSocketState.Open)
                {
                    // Отправляем ping, если соединение активно
                    var pingData = new { type = "ping" };
                    SendWebSocketMessage(pingData);
                    _isConnectionActive = true;
                }
                else
                {
                    // Попытка переподключения
                    _isConnectionActive = false;
                    Console.WriteLine("Соединение потеряно, пытаемся переподключиться...");

                    try
                    {
                        _webSocket?.Close();
                        _webSocket = new WebSocket("ws://blue.fnode.me:8114");

                        // Переподписываемся на события
                        _webSocket.OnMessage += OnWebSocketMessage;
                        _webSocket.OnOpen += (sender, e) =>
                        {
                            _isConnectionActive = true;

                            // После восстановления соединения повторно регистрируем устройство
                            if (_deviceData != null)
                            {
                                var registrationData = new { MAC = GetMacAddress(), DeviceName = _deviceData.DeviceName, Password = _deviceData.Password };
                                SendWebSocketMessage(registrationData);
                            }
                        };
                        _webSocket.OnClose += (sender, e) =>
                        {
                            Console.WriteLine("Соединение закрыто.");
                            _isConnectionActive = false;
                        };
                        _webSocket.OnError += (sender, e) =>
                        {
                            Console.WriteLine($"Ошибка: {e.Message}");
                            _isConnectionActive = false;
                        };

                        _webSocket.Connect();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при переподключении: {ex.Message}");
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



        public class DeviceData
        {
            public string DeviceName { get; set; }
            public string Password { get; set; }
        }

        public List<string> GetInstalledApplications()
        {
            var appList = new List<string>();
            var programsWithPaths = new List<string>
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files\Microsoft Office\root\Office16\excel.exe",
                @"C:\Program Files\Microsoft Office\root\Office16\powerpnt.exe"
            };
            // Экранирование обратных слешей
            foreach (var path in programsWithPaths)
            {
                if (File.Exists(path))
                    appList.Add(path.Replace("\\", "\\\\")); // Двойное экранирование
            }
            return appList;
        }
                //        @"C:\Program Files\Microsoft Office\root\Office16\winword.exe",
                //@"D:\Telegram Desktop\Telegram.exe"

        private async Task ReconnectWebSocket()
        {
            lock (_reconnectLock)
            {
                if (_webSocket?.ReadyState == WebSocketState.Open) return;
                if (_isReconnecting) return;
                _isReconnecting = true;
            }

            try
            {
                await Task.Delay(5000); // Задержка перед повторной попыткой

                Console.WriteLine("Попытка переподключения WebSocket...");

                _webSocket?.Close();

                // Создаем новый экземпляр WebSocket
                _webSocket = new WebSocket("ws://blue.fnode.me:8114");

                // Переназначаем обработчики событий
                _webSocket.OnMessage += OnWebSocketMessage;
                _webSocket.OnOpen += (sender, e) =>
                {
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
                        SendDataInternal(registrationData);
                    }

                    // Отправляем все команды из очереди
                    ProcessCommandQueue();
                };
                _webSocket.OnClose += (sender, e) =>
                {
                    Console.WriteLine("Соединение закрыто.");
                    _isConnectionActive = false;
                };
                _webSocket.OnError += (sender, e) =>
                {
                    Console.WriteLine($"Ошибка: {e.Message}");
                    _isConnectionActive = false;
                };

                // Пытаемся подключиться
                try
                {
                    _webSocket.Connect();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при подключении: {ex.Message}");
                }
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

        private void ProcessCommandQueue()
        {
            List<object> commandsToSend;

            // Блокируем очередь на время извлечения команд
            lock (_queueLock)
            {
                commandsToSend = new List<object>(_commandQueue);
                _commandQueue.Clear();
            }

            // Отправляем все команды из очереди
            foreach (var command in commandsToSend)
            {
                try
                {
                    SendDataInternal(command);
                }
                catch
                {
                    // В случае ошибки возвращаем команду в очередь
                    lock (_queueLock)
                    {
                        _commandQueue.Enqueue(command);
                    }
                    break; // Прерываем цикл для повторного подключения
                }
            }
        }
    }
}