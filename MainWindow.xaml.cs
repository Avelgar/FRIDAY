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

namespace Friday
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private VoiceService _voiceService;
        public static CommandManager _commandManager = new CommandManager();
        private static SettingManager _settingManager = new SettingManager();
        public AttachedFile _attachedFile;
        public AttachedFile GetAttachedFile()
        {
            return _attachedFile;
        }

        private dynamic _userData;

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

            if (string.IsNullOrEmpty(messageText))
            {
                ChatListBox.Items.Add("Сообщение не может быть пустым!" + Environment.NewLine);
                return;
            }

            var app = (App)Application.Current;
            if (app.IsWaitingForServerResponse)
            {
                ChatListBox.Items.Add("Ожидаю ответа от сервера. Пожалуйста, подождите..." + Environment.NewLine);
                ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
                return;
            }

            try
            {
                string screenshotBase64 = null;

                if (_attachedFile != null)
                {
                    screenshotBase64 = Convert.ToBase64String(_attachedFile.Data);
                }
                else if (IsScreenshotEnabled)
                {
                    byte[] screenshotBytes = _voiceService.CaptureScreenshot();
                    if (screenshotBytes != null)
                    {
                        screenshotBase64 = Convert.ToBase64String(screenshotBytes);
                    }
                }

                var message = new
                {
                    type = "текстовое сообщение",
                    command = messageText,
                    mac = GetMacAddress(),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    name = SettingManager.Setting.AssistantName,
                    screenshot = screenshotBase64
                };

                ((App)Application.Current).SendWebSocketMessage(message);

                ((App)Application.Current).MarkAsWaitingForServer();

                ChatListBox.Items.Add($"Вы: {messageText}{Environment.NewLine}");
                ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);

                MessageTextBox.Text = "";
                ClearAttachedFile();
            }
            catch (Exception ex)
            {
                ChatListBox.Items.Add($"Ошибка при отправке: {ex.Message}{Environment.NewLine}");
            }
        }
        public MainWindow(dynamic responseData = null)
        {
            InitializeComponent();
            _userData = responseData;
            InitializeUserInterface();
            LoadSettings();
            UpdateMicrophoneIcon(false);

            RenameService renameService = new RenameService(SettingManager.Setting.AssistantName, _settingManager);
            _voiceService = new VoiceService(renameService, _settingManager, this);
            _settingManager.SettingsChanged += SettingManager_SettingsChanged;

            ((App)Application.Current).VoiceService = _voiceService;
            ((App)Application.Current).IncrementWindowCount();

            _voiceService.OnMessageReceived += OnMessageReceived;
            CustomCommandService.Initialize(_voiceService);

            Commands = new ObservableCollection<Command>(_commandManager.GetCommands());
            CommandsItemsControl.ItemsSource = Commands;
            SearchTextBox.TextChanged += SearchTextBox_TextChanged;

            LoadActionTypes();
            ActionTypeComboBox.ItemsSource = ActionTypes;
            InputModeComboBox.SelectionChanged += InputModeComboBox_SelectionChanged;

            DataContext = this;
        }

        public void UpdateData(dynamic responseData)
        {
            if (responseData != null)
            {
                ChatListBox.Items.Add("Соединение восстановлено" + Environment.NewLine);
            }
        }

        private void InputModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InputModeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                SettingManager.Setting.InputMode = selectedItem.Content.ToString();
                _settingManager.SaveSettings();
            }
        }

        private void SettingManager_SettingsChanged(object sender, SettingChangedEventArgs e)
        {
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

        private void ProcessHistoryMessages(dynamic historyData)
        {
            if (historyData == null) return;

            foreach (var message in historyData)
            {
                string sender = message.sender?.ToString();
                string text = message.text?.ToString();

                if (string.IsNullOrEmpty(sender) || string.IsNullOrEmpty(text))
                    continue;

                if (sender == "Вы")
                {
                    ChatListBox.Items.Add($"{sender}: {text}{Environment.NewLine}");
                }
                else
                {
                    if (text.Contains("голосовой ответ|"))
                    {
                        var voiceResponses = new List<string>();
                        var parts = text.Split('⸵');

                        foreach (var part in parts)
                        {
                            if (part.StartsWith("голосовой ответ|"))
                            {
                                voiceResponses.Add(part.Substring("голосовой ответ|".Length));
                            }
                        }

                        if (voiceResponses.Count > 0)
                        {
                            string combinedResponse = string.Join(" ", voiceResponses);
                            ChatListBox.Items.Add($"{sender}: {combinedResponse}{Environment.NewLine}");
                        }
                    }
                    else if (text.Contains("текстовой ответ|"))
                    {
                        var textResponses = new List<string>();
                        var parts = text.Split('⸵');

                        foreach (var part in parts)
                        {
                            if (part.StartsWith("текстовой ответ|"))
                            {
                                textResponses.Add(part.Substring("текстовой ответ|".Length));
                            }
                        }

                        if (textResponses.Count > 0)
                        {
                            string combinedResponse = string.Join(" ", textResponses);
                            ChatListBox.Items.Add($"{sender}: {combinedResponse}{Environment.NewLine}");
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
                    ChatListBox.Items.Add($"Ошибка при отправке запроса: {ex.Message}" + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    ChatListBox.Items.Add($"Произошла ошибка: {ex.Message}" + Environment.NewLine);
                }
            }
        }

        private async void DisconnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            string targetMacAddress = button.Tag as string;
            if (string.IsNullOrEmpty(targetMacAddress)) return;

            try
            {
                string currentMacAddress = GetMacAddress();

                var message = new
                {
                    requester_mac = currentMacAddress,
                    target_mac = targetMacAddress
                };

                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(message);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://friday-assistant.ru/disconnect_device", content);
                    response.EnsureSuccessStatusCode();

                    var devicesTab = (TabItem)this.FindName("DevicesTab");
                    TabControl_SelectionChanged(null, new SelectionChangedEventArgs(TabControl.SelectionChangedEvent,
                        new List<object>(), new List<object> { devicesTab }));
                }
            }
            catch (Exception ex)
            {
                ChatListBox.Items.Add($"Ошибка при отключении устройства: {ex.Message}" + Environment.NewLine);
            }
        }

        private void AttachFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScreenshotButton.IsChecked == true)
            {
                ChatListBox.Items.Add("Нельзя прикреплять файлы в режиме скриншота!" + Environment.NewLine);
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Image Files (*.jpg; *.jpeg; *.png; *.bmp)|*.jpg; *.jpeg; *.png; *.bmp|All Files (*.*)|*.*";
            openFileDialog.Title = "Выберите файл для отправки";

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    ClearAttachedFile();

                    string filePath = openFileDialog.FileName;
                    string fileName = Path.GetFileName(filePath);
                    long fileSize = new FileInfo(filePath).Length;

                    if (fileSize > 10 * 1024 * 1024)
                    {
                        ChatListBox.Items.Add("Файл слишком большой! Максимальный размер: 10 МБ" + Environment.NewLine);
                        return;
                    }

                    byte[] fileBytes = File.ReadAllBytes(filePath);

                    _attachedFile = new AttachedFile
                    {
                        Name = fileName,
                        Data = fileBytes,
                        Size = fileSize
                    };

                    if (IsImageFile(fileName))
                    {
                        DisplayImageThumbnail(filePath);
                    }
                    _voiceService.AttachedFile = _attachedFile;
                }
                catch (Exception ex)
                {
                    ChatListBox.Items.Add($"Ошибка при загрузке файла: {ex.Message}" + Environment.NewLine);
                }
            }
        }

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

        private bool IsImageFile(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLower();
            return extension == ".jpg" || extension == ".jpeg" ||
                   extension == ".png" || extension == ".bmp";
        }

        private void DisplayImageThumbnail(string imagePath)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.DecodePixelWidth = 150;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                Border thumbnailContainer = new Border
                {
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(5),
                    CornerRadius = new CornerRadius(5),
                    Width = 160,
                    Height = 160
                };

                Grid grid = new Grid();

                Image thumbnail = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 150,
                    MaxHeight = 150
                };

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

                grid.Children.Add(thumbnail);
                grid.Children.Add(closeButton);

                thumbnailContainer.Child = grid;

                ThumbnailContainer.Items.Add(thumbnailContainer);
            }
            catch (Exception ex)
            {
                ChatListBox.Items.Add($"Не удалось загрузить миниатюру: {ex.Message}" + Environment.NewLine);
            }
        }

        public class AttachedFile
        {
            public string Name { get; set; }
            public byte[] Data { get; set; }
            public long Size { get; set; }
        }

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
                if (_userData.user_login != null && !string.IsNullOrEmpty(_userData.user_login.ToString()))
                {
                    ShowUserButton(_userData.user_login.ToString());
                }
                else
                {
                    ShowAuthButtons();
                }
            }
            else
            {
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
                int contentStart = line.IndexOf("): ") + 3;
                if (contentStart < 3) continue;

                string content = line.Substring(contentStart);
                string prefix = line.Substring(0, contentStart - 3);

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
                        ChatListBox.Items.Add($"Ошибка: {responseObject.message}" + Environment.NewLine);
                    }
                    else
                    {
                        ShowAuthButtons();
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                ChatListBox.Items.Add($"Ошибка при выходе из аккаунта: {ex.Message}" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                ChatListBox.Items.Add($"Произошла ошибка: {ex.Message}" + Environment.NewLine);
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

                ChatListBox.Items.Add($"Добро пожаловать, {username}!" + Environment.NewLine);
            });
        }

        private void OnMessageReceived(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ChatListBox.Items.Add(message + Environment.NewLine);
                ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
            });
        }

        public void LoadActionTypes()
        {
            var allActions = _commandManager.GetCommands()
                .SelectMany(c => c.Actions)
                .Select(a => a.ActionType)
                .Distinct()
                .ToList();

            allActions.Insert(0, "All");

            ActionTypes = allActions;
        }


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
                ChatListBox.Items.Add("Слушание остановлено." + Environment.NewLine);
            }
            else
            {
                _voiceService.ListeningState.StartListening();
                _voiceService.StartListening();
                UpdateMicrophoneIcon(true);
                ChatListBox.Items.Add("Начинаю слушать..." + Environment.NewLine);
            }
            ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
        }

        private void UpdateMicrophoneIcon(bool isListening)
        {
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
                LoadActionTypes();
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
            Commands = new ObservableCollection<Command>(_commandManager.GetCommands());
            CommandsItemsControl.ItemsSource = Commands;
            LoadActionTypes();
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

                                UpdateCommandsList();
                                LoadActionTypes();
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

                            UpdateCommandsList();
                            LoadActionTypes();
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
            FridayNameTextBox.Text = SettingManager.Setting.AssistantName;
            foreach (ComboBoxItem item in VoiceTypeComboBox.Items)
            {
                if (item.Content.ToString() == SettingManager.Setting.VoiceType)
                {
                    VoiceTypeComboBox.SelectedItem = item;
                    break;
                }
            }
            VolumeSlider.Value = SettingManager.Setting.Volume;

            string savedInputMode = SettingManager.Setting.InputMode;
            foreach (ComboBoxItem item in InputModeComboBox.Items)
            {
                if (item.Content.ToString() == savedInputMode)
                {
                    InputModeComboBox.SelectedItem = item;
                    break;
                }
            }

            MusicFolderPathTextBox.Text = SettingManager.Setting.MusicFolderPath;
        }

        private void ChooseMusicFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Выберите папку с музыкой",
                InitialDirectory = Directory.Exists(MusicFolderPathTextBox.Text)
                    ? MusicFolderPathTextBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            };

            if (dialog.ShowDialog() == true)
            {
                MusicFolderPathTextBox.Text = dialog.FolderName;
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
                password = SettingManager.Setting.Password;
            }

            _settingManager.UpdateSettings(assistantName, password, voiceType, volume, inputMode, MusicFolderPathTextBox.Text);
            MessageBox.Show("Настройки успешно обновлены!", "Успех!", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            ClearHistory();
        }

        public async void ClearHistory()
        {
            ChatListBox.Items.Clear();
            ChatListBox.Items.Add("История успешно очищена" + Environment.NewLine);
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

                    var response = await client.PostAsync("https://friday-assistant.ru/clear_history", content);
                    response.EnsureSuccessStatusCode();

                    var responseJson = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<dynamic>(responseJson);

                    if (responseObject.status != "success")
                    {
                        ChatListBox.Items.Add($"Ошибка: {responseObject.message}" + Environment.NewLine);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                ChatListBox.Items.Add($"Ошибка при отправке запроса: {ex.Message}" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                ChatListBox.Items.Add($"Произошла ошибка: {ex.Message}" + Environment.NewLine);
            }
        }

        public void ManageAppsButton_Click(object sender, RoutedEventArgs e)
        {
            var appPathsWindow = new Friday.Windows.AppPathsWindow();
            appPathsWindow.ShowDialog();
        }

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScreenshotButton.IsChecked == true)
            {
                _voiceService.IsScreenshotEnabled = true;
                if (_attachedFile != null)
                {
                    ChatListBox.Items.Add("Режим скриншота активирован. Прикрепленный файл удален." + Environment.NewLine);
                    ClearAttachedFile();
                }
            }
            else
            {
                _voiceService.IsScreenshotEnabled = false;
            }
            ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
        }

        public void ClearAttachedFile()
        {
            _attachedFile = null;
            ThumbnailContainer.Items.Clear();
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

        private void ChatListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChatListBox.SelectedItem != null)
                fillMessageTextBox(ChatListBox.SelectedItem.ToString());
        }

        public void fillMessageTextBox(string selectedText)
        {
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                return;
            }

            selectedText = new string(selectedText.Where(c => !char.IsControl(c)).ToArray()).Replace("Вы:", "").Replace("Бот:", "");
            if (!string.IsNullOrEmpty(selectedText))
            {
                MessageTextBox.Text = selectedText;
            }
        }

        private void ChatMessageContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement messageContainer)
            {
                return;
            }

            if (messageContainer.FindName("RegenerateButton") is Button regenerateButton)
            {
                string messageText = messageContainer.DataContext as string;
                regenerateButton.Visibility = IsLastBotMessage(messageText)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void ChatMessage_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement clickedElement)
            {
                return;
            }

            string messageText = clickedElement.DataContext as string;
            fillMessageTextBox(messageText);
        }

        private void EditChatMessage_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Редактирование временно недоступно.", "Заглушка", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CopyChatMessage_Click(object sender, RoutedEventArgs e)
        {
            string messageText = GetMessageFromSender(sender);
            if (string.IsNullOrWhiteSpace(messageText))
            {
                return;
            }

            string cleanText = new string(messageText.Where(c => !char.IsControl(c)).ToArray()).Trim();

            if (cleanText.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase))
            {
                cleanText = cleanText.Substring("Вы:".Length).Trim();
            }
            else if (cleanText.StartsWith("Бот:", StringComparison.OrdinalIgnoreCase))
            {
                cleanText = cleanText.Substring("Бот:".Length).Trim();
            }

            Clipboard.SetText(cleanText);
        }

        private void DeleteChatMessage_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Удаление временно недоступно.", "Заглушка", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void RegenerateChatMessage_Click(object sender, RoutedEventArgs e)
        {
            int botMessageIndex = GetChatMessageIndexFromSender(sender);
            if (botMessageIndex < 0 || botMessageIndex >= ChatListBox.Items.Count)
            {
                return;
            }

            if (ChatListBox.Items[botMessageIndex] is not string botMessage || !IsLastBotMessage(botMessage))
            {
                return;
            }

            int previousUserIndex = FindPreviousUserMessageIndex(botMessageIndex);
            if (previousUserIndex < 0)
            {
                ChatListBox.Items.Add("Не найдено предыдущее сообщение пользователя для перегенерации." + Environment.NewLine);
                ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
                return;
            }

            string previousUserMessage = ChatListBox.Items[previousUserIndex] as string;
            string userText = ExtractMessageContent(previousUserMessage, "Вы:");

            if (string.IsNullOrWhiteSpace(userText))
            {
                ChatListBox.Items.Add("Не удалось извлечь текст запроса для перегенерации." + Environment.NewLine);
                ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
                return;
            }

            MessageTextBox.Text = userText;
            await SendCurrentMessageAsync();
        }

        private string GetMessageFromSender(object sender)
        {
            if (sender is not DependencyObject source)
            {
                return null;
            }

            ListBoxItem listBoxItem = FindParent<ListBoxItem>(source);
            return listBoxItem?.DataContext as string;
        }

        private int GetChatMessageIndexFromSender(object sender)
        {
            if (sender is not DependencyObject source)
            {
                return -1;
            }

            ListBoxItem listBoxItem = FindParent<ListBoxItem>(source);
            if (listBoxItem == null)
            {
                return -1;
            }

            return ChatListBox.ItemContainerGenerator.IndexFromContainer(listBoxItem);
        }

        private bool IsLastBotMessage(string message)
        {
            if (!IsBotMessage(message))
            {
                return false;
            }

            for (int i = ChatListBox.Items.Count - 1; i >= 0; i--)
            {
                if (ChatListBox.Items[i] is string current && IsBotMessage(current))
                {
                    return string.Equals(current, message, StringComparison.Ordinal);
                }
            }

            return false;
        }

        private bool IsBotMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string cleanMessage = new string(message.Where(c => !char.IsControl(c)).ToArray()).Trim();
            string assistantPrefix = $"{SettingManager.Setting.AssistantName}:";

            return cleanMessage.StartsWith("Бот:", StringComparison.OrdinalIgnoreCase)
                || cleanMessage.StartsWith(assistantPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private int FindPreviousUserMessageIndex(int startIndex)
        {
            for (int i = startIndex - 1; i >= 0; i--)
            {
                if (ChatListBox.Items[i] is string current)
                {
                    string clean = new string(current.Where(c => !char.IsControl(c)).ToArray()).Trim();
                    if (clean.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private string ExtractMessageContent(string message, string prefix)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            string clean = new string(message.Where(c => !char.IsControl(c)).ToArray());
            if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return clean.Substring(prefix.Length).Trim();
            }

            return clean.Trim();
        }
    }
    public enum InputMode
    {
        NameResponseCommand,
        NamePlusCommand,
        Conversation
    }
}

