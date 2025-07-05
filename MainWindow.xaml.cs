using System.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using Friday;
using System.Windows.Media;
using System.Windows.Input;
using Friday.Managers;
using System.Net.NetworkInformation;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using Friday.Services;
using FigmaToWpf;
using System.Windows.Media.Imaging;
using System.Drawing;

namespace FigmaToWpf
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private VoiceService _voiceService;
        public static Friday.CommandManager _commandManager = new Friday.CommandManager();
        private static SettingManager _settingManager = new SettingManager();

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

        public MainWindow(dynamic responseData = null)
        {
            InitializeComponent();
            _userData = responseData;
            InitializeUserInterface();
            LoadSettings();
            UpdateMicrophoneIcon(false);

            RenameService renameService = new RenameService(_settingManager.Setting.AssistantName);
            _voiceService = new VoiceService(renameService, _settingManager);

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

                        var response = await client.PostAsync("http://blue.fnode.me:25550/get_devices", content);
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

                    var response = await client.PostAsync("http://blue.fnode.me:25550/disconnect_device", content);
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
                ConsoleTextBox.AppendText(_userData.history.ToString() + Environment.NewLine);
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

                    var response = await client.PostAsync("http://blue.fnode.me:25550/logout", content);
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
        }
        public void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            string assistantName = FridayNameTextBox.Text;
            string password = PasswordTextBox.Text;
            string voiceType = VoiceTypeComboBox.Text;
            int volume = Convert.ToInt32(VolumeSlider.Value);
            if (string.IsNullOrEmpty(assistantName) || string.IsNullOrEmpty(voiceType))
            {
                MessageBox.Show("Поля не могут быть пустыми", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(password)) { password = _settingManager.Setting.Password; }
            _settingManager.UpdateSettings(assistantName, password, voiceType, volume);
            MessageBox.Show("Настройки успешно обновлены!", "Успех!", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
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

                    var response = await client.PostAsync("http://blue.fnode.me:25550/clear_history", content);
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

        public void ChangedataButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeDataWindow changedatawindow = new ChangeDataWindow();
            changedatawindow.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            ((App)Application.Current).DecrementWindowCount();
            base.OnClosed(e);
        }
    }
}

//gemini-2.0-flash
