using Emgu.CV;
using Friday;
using Friday.Managers;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FigmaToWpf
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private VoiceService _voiceService;
        public static Friday.CommandManager _commandManager = new Friday.CommandManager();
        private static SettingManager _settingManager = new SettingManager();
        public AttachedFile _attachedFile;
        public AttachedFile GetAttachedFile()
        {
            return _attachedFile;
        }

        private dynamic _userData;

        // Добавляем свойство для отслеживания команд, отображаемых в ItemsControl.
        private ObservableCollection<Command> _commands;
        public ObservableCollection<Command> Commands
        {
            get { return _commands; }
            set
            {
                _commands = value;
                OnPropertyChanged(nameof(Commands));
            }
        }
       
        private List<string> _actionTypes;
        public List<string> ActionTypes
        {
            get { return _actionTypes; }
            set
            {
                _actionTypes = value;
                OnPropertyChanged(nameof(ActionTypes));
            }
        }

        public bool IsScreenshotEnabled => ScreenshotButton?.IsChecked == true;
        private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            await SendCurrentMessageAsync();
        }

        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SendCurrentMessageAsync().ConfigureAwait(false);
            }
        }

        private async Task SendCurrentMessageAsync()
        {
            string messageText = MessageTextBox.Text.Trim();

            // Проверяем, что есть текст сообщения
            if (string.IsNullOrEmpty(messageText))
            {
                ConsoleTextBox.AppendText("Сообщение не может быть пустым!" + Environment.NewLine);
                return;
            }

            try
            {
                string screenshotBase64 = null;

                // Приоритет: сначала проверяем прикрепленный файл
                if (_attachedFile != null)
                {
                    screenshotBase64 = Convert.ToBase64String(_attachedFile.Data);
                }
                // Если файла нет, но включен режим скриншота - делаем скриншот
                else if (IsScreenshotEnabled)
                {
                    byte[] screenshotBytes = _voiceService.CaptureScreenshot();
                    if (screenshotBytes != null)
                    {
                        screenshotBase64 = Convert.ToBase64String(screenshotBytes);
                    }
                }

                // Формируем объект сообщения
                var message = new
                {
                    type = "текстовое сообщение",
                    command = messageText,
                    mac = GetMacAddress(),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    name = _settingManager.Setting.AssistantName,
                    screenshot = screenshotBase64
                };

                // Отправка через WebSocket
                ((App)Application.Current).SendWebSocketMessage(message);

                // Выводим сообщение в консоль
                ConsoleTextBox.AppendText($"Вы: {messageText}{Environment.NewLine}");

                ConsoleTextBox.ScrollToEnd();

                // Очищаем поле ввода и информацию о файле
                MessageTextBox.Text = "";
                ClearAttachedFile();
            }
            catch (Exception ex)
            {
                ConsoleTextBox.AppendText($"Ошибка при отправке: {ex.Message}{Environment.NewLine}");
            }
        }
        public MainWindow(dynamic responseData = null)
        {
            InitializeComponent();
            _userData = responseData;
            InitializeUserInterface();
            LoadSettings();
            UpdateMicrophoneIcon(false);

            RenameService renameService = new RenameService(_settingManager.Setting.AssistantName, _settingManager);
            _voiceService = new VoiceService(renameService, _settingManager, this);
            _settingManager.SettingsChanged += SettingManager_SettingsChanged;

            ((App)Application.Current).VoiceService = _voiceService;
            ((App)Application.Current).IncrementWindowCount();

            _voiceService.OnMessageReceived += OnMessageReceived;
            CustomCommandService.Initialize(_voiceService);

            // Инициализируем Commands и подписыва  емся на изменение текста в SearchTextBox
            Commands = new ObservableCollection<Command>(_commandManager.GetCommands());
            CommandsItemsControl.ItemsSource = Commands;  // Привязка к Commands, а не напрямую к _commandManager
            SearchTextBox.TextChanged += SearchTextBox_TextChanged; // Подписываемся на событие изменения текста

            // Заполняем ActionTypes и ComboBox
            LoadActionTypes();
            ActionTypeComboBox.ItemsSource = ActionTypes;
            InputModeComboBox.SelectionChanged += InputModeComboBox_SelectionChanged;

            DataContext = this; // Необходимо для работы привязки Commands
        }

        public void UpdateData(dynamic responseData)
        {
            // Здесь обновляем данные в окне без его повторного открытия
            // Например, обновляем статус соединения или другие элементы UI
            if (responseData != null)
            {
                ConsoleTextBox.AppendText("Соединение восстановлено" + Environment.NewLine);
                // Другие обновления по необходимости
            }
        }

        private void InputModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InputModeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                _settingManager.Setting.InputMode = selectedItem.Content.ToString();
                _settingManager.SaveSettings();
            }
        }

        private void SettingManager_SettingsChanged(object sender, SettingChangedEventArgs e)
        {
            // Обновляем UI в основном потоке
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(e.AssistantName))
                {
                    FridayNameTextBox.Text = e.AssistantName;
                }

                if (!string.IsNullOrEmpty(e.VoiceType))
                {
                    foreach (ComboBoxItem item in VoiceTypeComboBox.Items)
                    {
                        if (item.Content.ToString() == e.VoiceType)
                        {
                            VoiceTypeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
            });
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

            return string.Empty; 
        }

        private void ProcessHistoryMessages(dynamic historyData)
        {
            if (historyData == null) return;

            foreach (var message in historyData)
            {
                string sender = message.sender?.ToString();
                string text = message.text?.ToString();

                if (string.IsNullOrEmpty(sender) || string.IsNullOrEmpty(text))
                    continue;

                // Для сообщений от пользователя
                if (sender == "Вы")
                {
                    ConsoleTextBox.AppendText($"{sender}: {text}{Environment.NewLine}");
                }
                // Для сообщений от бота или устройств
                else
                {
                    // Обрабатываем только сообщения, содержащие голосовые ответы
                    if (text.Contains("голосовой ответ|"))
                    {
                        // Извлекаем все голосовые ответы
                        var voiceResponses = new List<string>();
                        var parts = text.Split('⸵');

                        foreach (var part in parts)
                        {
                            if (part.StartsWith("голосовой ответ|"))
                            {
                                voiceResponses.Add(part.Substring("голосовой ответ|".Length));
                            }
                        }

                        // Если нашли голосовые ответы - объединяем их
                        if (voiceResponses.Count > 0)
                        {
                            string combinedResponse = string.Join(" ", voiceResponses);
                            ConsoleTextBox.AppendText($"{sender}: {combinedResponse}{Environment.NewLine}");
                        }
                    }
                    else if (text.Contains("текстовой ответ|"))
                    {
                        // Извлекаем все голосовые ответы
                        var textResponses = new List<string>();
                        var parts = text.Split('⸵');

                        foreach (var part in parts)
                        {
                            if (part.StartsWith("текстовой ответ|"))
                            {
                                textResponses.Add(part.Substring("текстовой ответ|".Length));
                            }
                        }

                        // Если нашли голосовые ответы - объединяем их
                        if (textResponses.Count > 0)
                        {
                            string combinedResponse = string.Join(" ", textResponses);
                            ConsoleTextBox.AppendText($"{sender}: {combinedResponse}{Environment.NewLine}");
                        }
                    }
                }
            }
        }


        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;

            if (e.AddedItems[0] is TabItem selectedTab && selectedTab.Header.ToString() == "Устройства")
            {
                try
                {
                    var message = new
                    {
                        mac = GetMacAddress()
                    };

                    using (var client = new HttpClient())
                    {
                        var json = JsonConvert.SerializeObject(message);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await client.PostAsync("https://friday-assistant.ru/get_devices", content);
                        response.EnsureSuccessStatusCode();

                        var responseJson = await response.Content.ReadAsStringAsync();
                        var responseObject = JsonConvert.DeserializeObject<DeviceResponse>(responseJson);

                        Dispatcher.Invoke(() =>
                        {
                            // Устройства аккаунта
                            if (responseObject.account_devices != null && responseObject.account_devices.Count > 0)
                            {
                                AccountDevicesList.ItemsSource = responseObject.account_devices;
                                NoAccountDevicesText.Visibility = Visibility.Collapsed;
                            }
                            else
                            {
                                AccountDevicesList.ItemsSource = null;
                                NoAccountDevicesText.Visibility = Visibility.Visible;
                            }

                            // Подключенные устройства
                            if (responseObject.my_devices != null && responseObject.my_devices.Count > 0)
                            {
                                ConnectedDevicesList.ItemsSource = responseObject.my_devices;
                                NoConnectedDevicesText.Visibility = Visibility.Collapsed;
                            }
                            else
                            {
                                ConnectedDevicesList.ItemsSource = null;
                                NoConnectedDevicesText.Visibility = Visibility.Visible;
                            }
                        });
                    }
                }
                catch (HttpRequestException ex)
                {
                    ConsoleTextBox.AppendText($"Ошибка при отправке запроса: {ex.Message}" + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    ConsoleTextBox.AppendText($"Произошла ошибка: {ex.Message}" + Environment.NewLine);
                }
            }
        }

        private async void DisconnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            // MAC устройства, которое нужно отключить (из Tag кнопки)
            string targetMacAddress = button.Tag as string;
            if (string.IsNullOrEmpty(targetMacAddress)) return;

            try
            {
                // Получаем MAC текущего устройства
                string currentMacAddress = GetMacAddress();

                var message = new
                {
                    requester_mac = currentMacAddress,  // MAC устройства, которое инициирует отключение
                    target_mac = targetMacAddress      // MAC устройства, которое нужно отключить
                };

                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(message);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://friday-assistant.ru/disconnect_device", content);
                    response.EnsureSuccessStatusCode();

                    // Обновляем список устройств
                    var devicesTab = (TabItem)this.FindName("DevicesTab");
                    TabControl_SelectionChanged(null, new SelectionChangedEventArgs(TabControl.SelectionChangedEvent,
                        new List<object>(), new List<object> { devicesTab }));
                }
            }
            catch (Exception ex)
            {
                ConsoleTextBox.AppendText($"Ошибка при отключении устройства: {ex.Message}" + Environment.NewLine);
            }
        }

        private void AttachFileButton_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, не активирован ли режим скриншота
            if (ScreenshotButton.IsChecked == true)
            {
                ConsoleTextBox.AppendText("Нельзя прикреплять файлы в режиме скриншота!" + Environment.NewLine);
                return;
            }

            // Создаем диалог выбора файла
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Image Files (*.jpg; *.jpeg; *.png; *.bmp)|*.jpg; *.jpeg; *.png; *.bmp|All Files (*.*)|*.*";
            openFileDialog.Title = "Выберите файл для отправки";

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    // Очищаем предыдущий файл
                    ClearAttachedFile();

                    // Получаем информацию о файле
                    string filePath = openFileDialog.FileName;
                    string fileName = Path.GetFileName(filePath);
                    long fileSize = new FileInfo(filePath).Length;

                    // Проверяем размер файла (например, ограничим 10 МБ)
                    if (fileSize > 10 * 1024 * 1024)
                    {
                        ConsoleTextBox.AppendText("Файл слишком большой! Максимальный размер: 10 МБ" + Environment.NewLine);
                        return;
                    }

                    // Читаем файл в массив байтов
                    byte[] fileBytes = File.ReadAllBytes(filePath);

                    // Сохраняем файл для последующей отправки
                    _attachedFile = new AttachedFile
                    {
                        Name = fileName,
                        Data = fileBytes,
                        Size = fileSize
                    };

                    // Отображаем миниатюру для изображений
                    if (IsImageFile(fileName))
                    {
                        DisplayImageThumbnail(filePath);
                    }
                    _voiceService.AttachedFile = _attachedFile;
                }
                catch (Exception ex)
                {
                    ConsoleTextBox.AppendText($"Ошибка при загрузке файла: {ex.Message}" + Environment.NewLine);
                }
            }
        }

        // Вспомогательный метод для форматирования размера файла
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        // Проверяем, является ли файл изображением
        private bool IsImageFile(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLower();
            return extension == ".jpg" || extension == ".jpeg" ||
                   extension == ".png" || extension == ".bmp";
        }

        // Отображаем миниатюру изображения
        private void DisplayImageThumbnail(string imagePath)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.DecodePixelWidth = 150; // Увеличиваем размер миниатюры
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                // Создаем контейнер для миниатюры с возможностью закрытия
                Border thumbnailContainer = new Border
                {
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(5),
                    CornerRadius = new CornerRadius(5),
                    Width = 160, // Ширина контейнера
                    Height = 160 // Высота контейнера
                };

                // Создаем сетку для размещения изображения и кнопки закрытия
                Grid grid = new Grid();

                // Изображение
                Image thumbnail = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform, // Сохраняем пропорции
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 150,
                    MaxHeight = 150
                };

                // Кнопка закрытия
                Button closeButton = new Button
                {
                    Content = "×",
                    Width = 20,
                    Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 2, 0),
                    Padding = new Thickness(0),
                    FontWeight = FontWeights.Bold,
                    Background = Brushes.White,
                    Foreground = Brushes.Black,
                    BorderThickness = new Thickness(0)
                };

                closeButton.Click += (s, e) => ClearAttachedFile();

                // Добавляем элементы в сетку
                grid.Children.Add(thumbnail);
                grid.Children.Add(closeButton);

                // Добавляем сетку в контейнер
                thumbnailContainer.Child = grid;

                // Добавляем контейнер в интерфейс
                ThumbnailContainer.Items.Add(thumbnailContainer);
            }
            catch (Exception ex)
            {
                ConsoleTextBox.AppendText($"Не удалось загрузить миниатюру: {ex.Message}" + Environment.NewLine);
            }
        }

        public class AttachedFile
        {
            public string Name { get; set; }
            public byte[] Data { get; set; }
            public long Size { get; set; }
        }

        // Классы для десериализации JSON остаются без изменений
        public class DeviceResponse
        {
            public List<DeviceInfo> account_devices { get; set; }
            public List<DeviceInfo> my_devices { get; set; }
            public string status { get; set; }
        }

        public class DeviceInfo
        {
            public string DeviceName { get; set; }
            public string MacAddress { get; set; }
            public bool IsOnline { get; set; }
            public bool IsAccountDevice { get; set; }
        }


        private void ConnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            var connectDeviceWindow = new ConnectDeviceWindow();
            if (connectDeviceWindow.ShowDialog() == true)
            {
                // Обновить список устройств после успешного подключения
                TabControl_SelectionChanged(null, new SelectionChangedEventArgs(
                    TabControl.SelectionChangedEvent,
                    new List<object>(),
                    new List<object> { DevicesTab }));
            }
        }

        private void InitializeUserInterface()
        {
            if (_userData != null)
            {
                // Проверяем наличие user_login
                if (_userData.user_login != null && !string.IsNullOrEmpty(_userData.user_login.ToString()))
                {
                    ShowUserButton(_userData.user_login.ToString());
                }
                else
                {
                    // По умолчанию показываем кнопки авторизации
                    ShowAuthButtons();
                }
            }
            else
            {
                // Если данных нет, показываем кнопки авторизации
                ShowAuthButtons();
            }

            if (_userData.history != null)
            {
                ProcessHistoryMessages(_userData.history);
            }
        }

        private string ProcessHistory(string history)
        {
            var result = new StringBuilder();
            var lines = history.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line));

            foreach (var line in lines)
            {
                // Извлекаем основное содержимое после префикса
                int contentStart = line.IndexOf("): ") + 3;
                if (contentStart < 3) continue;

                string content = line.Substring(contentStart);
                string prefix = line.Substring(0, contentStart - 3);

                // Упрощаем префиксы (убираем скобки с временем)
                if (prefix.StartsWith("Вы ("))
                {
                    result.AppendLine($"Вы: {content}");
                }
                else if (prefix.StartsWith("Бот ("))
                {
                    result.AppendLine($"Бот: {content}");
                }
                else
                {
                    // Для других устройств сохраняем оригинальный формат
                    result.AppendLine($"{prefix}: {content}");
                }
            }

            return result.ToString();
        }

        public void ShowUserButton(string username)
        {
            UserButtonText.Text = username;
            UserButton.Visibility = Visibility.Visible;
            LoginButton.Visibility = Visibility.Collapsed;
            RegisterButton.Visibility = Visibility.Collapsed;
        }

        private void ShowAuthButtons()
        {
            UserButton.Visibility = Visibility.Collapsed;
            LoginButton.Visibility = Visibility.Visible;
            RegisterButton.Visibility = Visibility.Visible;
        }

        private void UserButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            var logoutItem = new MenuItem { Header = "Выйти" };
            logoutItem.Click += (s, args) => Logout();

            menu.Items.Add(logoutItem);

            menu.PlacementTarget = sender as Button;
            menu.IsOpen = true;
        }

        private async void Logout()
        {
            try
            {
                // Отправляем команду на сервер о выходе
                var logoutCommand = new
                {
                    MAC = GetMacAddress(),
                    Command = "logout"
                };

                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(logoutCommand);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://friday-assistant.ru/logout", content);
                    response.EnsureSuccessStatusCode();

                    var responseJson = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<dynamic>(responseJson);

                    if (responseObject.status != "success")
                    {
                        ConsoleTextBox.AppendText($"Ошибка: {responseObject.message}" + Environment.NewLine);
                    }
                    else
                    {
                        ShowAuthButtons();
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                ConsoleTextBox.AppendText($"Ошибка при выходе из аккаунта: {ex.Message}" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                ConsoleTextBox.AppendText($"Произошла ошибка: {ex.Message}" + Environment.NewLine);
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new LoginWindow();
            loginWindow.ShowDialog();
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e) 
        {
            var registerWindow = new RegisterWindow();
            registerWindow.ShowDialog();
        }

        public void UpdateAfterRegistration(string username)
        {
            Dispatcher.Invoke(() =>
            {
                UserButtonText.Text = username;
                UserButton.Visibility = Visibility.Visible;
                LoginButton.Visibility = Visibility.Collapsed;
                RegisterButton.Visibility = Visibility.Collapsed;

                ConsoleTextBox.AppendText($"Добро пожаловать, {username}!" + Environment.NewLine);
            });
        }

        private void OnMessageReceived(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ConsoleTextBox.AppendText(message + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            });
        }

        public void LoadActionTypes()
        {
            // Получаем все типы действий из команд
            var allActions = _commandManager.GetCommands()
                .SelectMany(c => c.Actions)
                .Select(a => a.ActionType) // Changed a.Type to a.ActionType
                .Distinct()
                .ToList();

            // Добавляем пустой тип для отображения всех команд
            allActions.Insert(0, "All");

            // Обновляем ActionTypes и вызываем PropertyChanged
            ActionTypes = allActions;
        }


        // Реализация INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterCommands();
        }
        public void ActionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterCommands();
        }

        public void FilterCommands()
        {
            string searchText = SearchTextBox.Text;
            string selectedActionType = ActionTypeComboBox.SelectedItem as string;

            IEnumerable<Command> filteredCommands = _commandManager.GetCommands();

            if (!string.IsNullOrEmpty(searchText) && searchText != "Search")
            {
                filteredCommands = filteredCommands.Where(c =>
                    c.Name.ToLower().Contains(searchText.ToLower()) ||
                    c.Description.ToLower().Contains(searchText.ToLower()));
            }

            if (!string.IsNullOrEmpty(selectedActionType) && selectedActionType != "All")
            {
                filteredCommands = filteredCommands.Where(c => c.Actions.Any(a => a.ActionType == selectedActionType)); // Changed a.Type to a.ActionType
            }

            Commands = new ObservableCollection<Command>(filteredCommands.ToList());
            CommandsItemsControl.ItemsSource = Commands;
        }
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_voiceService.ListeningState.IsListening())
            {
                _voiceService.ListeningState.StopListening();
                _voiceService.StopListening();
                UpdateMicrophoneIcon(false);
                ConsoleTextBox.AppendText("Слушание остановлено." + Environment.NewLine);
            }
            else
            {
                _voiceService.ListeningState.StartListening();
                _voiceService.StartListening();
                UpdateMicrophoneIcon(true);
                ConsoleTextBox.AppendText("Начинаю слушать..." + Environment.NewLine);
            }
            ConsoleTextBox.ScrollToEnd();
        }

        private void UpdateMicrophoneIcon(bool isListening)
        {
            // Эмодзи для разных состояний
            ListenButton.Content = isListening ? "🔴" : "🎤";
            ListenButton.Foreground = isListening ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.White;
        }

        public void AddCommandButton_Click(object sender, RoutedEventArgs e)
        {
            var addCommandWindow = new AddCommandWindow("Добавить команду");

            if (addCommandWindow.ShowDialog() == true)
            {
                string name = addCommandWindow.CommandName;
                string description = addCommandWindow.Description;
                var actions = addCommandWindow.Actions;
                bool isPasswordSet = addCommandWindow.IsPasswordSet;

                var customCommand = _commandManager.FindCommandByTrigger(name);
                if (customCommand != null)
                {
                    MessageBox.Show("Команда уже существует!", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                MessageBox.Show("Команда добавлена успешно!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                _commandManager.AddCommand(name, description, actions, isPasswordSet);

                UpdateCommandsList();
                LoadActionTypes(); //call it here
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        public void UpdateCommandsList()
        {
            // После обновления команд в _commandManager, обновляем и Commands, чтобы отобразить изменения
            Commands = new ObservableCollection<Command>(_commandManager.GetCommands());
            CommandsItemsControl.ItemsSource = Commands; // Обновляем ItemsSource
            LoadActionTypes(); //call it here
        }
        public void EditCommandButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                var border = FindParent<Border>(button);
                if (border != null)
                {
                    var nameTextBlock = FindChild<TextBlock>(border, "NameTextBlock");
                    if (nameTextBlock != null)
                    {
                        string commandName = nameTextBlock.Text.Trim();
                        var commandToEdit = _commandManager.GetCommands().FirstOrDefault(c => c.Name == commandName);

                        if (commandToEdit != null)
                        {
                            var addCommandWindow = new AddCommandWindow("Изменить команду");
                            addCommandWindow.Initialize(commandToEdit.Name, commandToEdit.Description, commandToEdit.Actions, commandToEdit.IsPassword);

                            if (addCommandWindow.ShowDialog() == true)
                            {
                                string newName = addCommandWindow.CommandName;
                                string newDescription = addCommandWindow.Description;
                                var newActions = addCommandWindow.Actions;
                                bool isPasswordSet = addCommandWindow.IsPasswordSet;

                                _commandManager.EditCommand(commandToEdit.Id, newName, newDescription, newActions, isPasswordSet);

                                UpdateCommandsList(); //call it here
                                LoadActionTypes(); //call it here
                            }
                        }
                    }
                }
            }
        }



        public void DeleteCommandButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                var border = FindParent<Border>(button);
                if (border != null)
                {
                    var nameTextBlock = FindChild<TextBlock>(border, "NameTextBlock");
                    if (nameTextBlock != null)
                    {
                        string commandName = nameTextBlock.Text.Trim();
                        MessageBoxResult result = MessageBox.Show($"Вы уверены, что хотите удалить команду: {commandName}?", "Подтверждение удаления", MessageBoxButton.YesNo);

                        if (result == MessageBoxResult.Yes)
                        {
                            _commandManager.DeleteCommand(commandName);

                            UpdateCommandsList(); //call it here
                            LoadActionTypes(); //call it here
                        }
                    }
                }
            }
        }
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;

            T parent = parentObject as T;
            return parent != null ? parent : FindParent<T>(parentObject);
        }

        private T FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            T foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                T childType = child as T;
                if (childType != null && !string.IsNullOrEmpty(childName))
                {
                    var frameworkElement = child as FrameworkElement;
                    if (frameworkElement != null && frameworkElement.Name == childName)
                    {
                        foundChild = (T)child;
                        break;
                    }
                }
                else
                {
                    foundChild = FindChild<T>(child, childName);
                    if (foundChild != null) break;
                }
            }

            return foundChild;
        }
        private void LoadSettings()
        {
            // Существующие настройки...
            FridayNameTextBox.Text = _settingManager.Setting.AssistantName;
            foreach (ComboBoxItem item in VoiceTypeComboBox.Items)
            {
                if (item.Content.ToString() == _settingManager.Setting.VoiceType)
                {
                    VoiceTypeComboBox.SelectedItem = item;
                    break;
                }
            }
            VolumeSlider.Value = _settingManager.Setting.Volume;

            // Загрузка режима ввода
            string savedInputMode = _settingManager.Setting.InputMode;
            foreach (ComboBoxItem item in InputModeComboBox.Items)
            {
                if (item.Content.ToString() == savedInputMode)
                {
                    InputModeComboBox.SelectedItem = item;
                    break;
                }
            }
        }
        public void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            string assistantName = FridayNameTextBox.Text;
            string password = PasswordTextBox.Text;
            string voiceType = (VoiceTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            int volume = Convert.ToInt32(VolumeSlider.Value);
            string inputMode = (InputModeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();

            if (string.IsNullOrEmpty(assistantName) || string.IsNullOrEmpty(voiceType) || string.IsNullOrEmpty(inputMode))
            {
                MessageBox.Show("Поля не могут быть пустыми", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                password = _settingManager.Setting.Password;
            }

            _settingManager.UpdateSettings(assistantName, password, voiceType, volume, inputMode);
            MessageBox.Show("Настройки успешно обновлены!", "Успех!", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            ClearHistory();
        }

        public async void ClearHistory()
        {
            ConsoleTextBox.Clear();
            ConsoleTextBox.AppendText("История успешно очищена" + Environment.NewLine);
            try
            {
                var message = new
                {
                    mac = GetMacAddress() // Замените на реальный MAC адрес устройства
                };

                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(message);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://friday-assistant.ru/clear_history", content);
                    response.EnsureSuccessStatusCode();

                    var responseJson = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<dynamic>(responseJson);

                    if (responseObject.status != "success")
                    {
                        ConsoleTextBox.AppendText($"Ошибка: {responseObject.message}" + Environment.NewLine);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                ConsoleTextBox.AppendText($"Ошибка при отправке запроса: {ex.Message}" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                ConsoleTextBox.AppendText($"Произошла ошибка: {ex.Message}" + Environment.NewLine);
            }
        }

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScreenshotButton.IsChecked == true)
            {
                _voiceService.IsScreenshotEnabled = true;
                // Если был прикреплен файл, очищаем его
                if (_attachedFile != null)
                {
                    ConsoleTextBox.AppendText("Режим скриншота активирован. Прикрепленный файл удален." + Environment.NewLine);
                    ClearAttachedFile();
                }
            }
            else
            {
                _voiceService.IsScreenshotEnabled = false;
            }
            ConsoleTextBox.ScrollToEnd();
        }

        public void ClearAttachedFile()
        {
            _attachedFile = null;
            ThumbnailContainer.Items.Clear(); // Используем Items вместо Children
        }

        public void ChangedataButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeDataWindow changedatawindow = new ChangeDataWindow();
            changedatawindow.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            _settingManager.SettingsChanged -= SettingManager_SettingsChanged;
            ((App)Application.Current).DecrementWindowCount();
            base.OnClosed(e);
        }
    }
    public enum InputMode
    {
        NameResponseCommand,
        NamePlusCommand,
        Conversation
    }
}

