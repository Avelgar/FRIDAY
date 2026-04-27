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
using System.Windows.Data;
using System.Globalization;

namespace Friday
{
    public class ChatMessage : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Sender { get; set; }
        public string Text { get; set; }
        public bool IsLocal { get; set; }

        public bool IsUser => Sender == "Вы";

        private string _displayText;
        public string DisplayText
        {
            get => _displayText ?? Text;
            set { _displayText = value; OnPropertyChanged(nameof(DisplayText)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private VoiceService _voiceService;
        public static CommandManager _commandManager = new CommandManager();
        private static SettingManager _settingManager = new SettingManager();
        public AttachedFile _attachedFile;

        // Переменная для хранения индекса редактируемого сообщения
        private int _editingMessageIndex = -1;
        public ObservableCollection<ChatMessage> ChatMessages { get; set; } = new ObservableCollection<ChatMessage>();
        private ChatMessage _editingMessage = null;

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
            // Отмена редактирования по нажатию Esc
            else if (e.Key == Key.Escape && _editingMessageIndex != -1)
            {
                _editingMessageIndex = -1;
                SendMessageButton.Content = "Отправить";
                MessageTextBox.Text = "";
            }
        }

        private async Task SendCurrentMessageAsync()
        {
            string messageText = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(messageText)) return;

            // РЕЖИМ РЕДАКТИРОВАНИЯ
            if (_editingMessage != null)
            {
                _editingMessage.Text = messageText;
                _editingMessage.DisplayText = messageText; // Обновит UI

                _editingMessage = null;
                SendMessageButton.Content = "Отправить";
                MessageTextBox.Text = "";
                return;
            }

            var app = (App)Application.Current;
            if (app.IsWaitingForServerResponse)
            {
                ShowSystemMessage("Ожидаю ответа от сервера. Пожалуйста, подождите...");
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

                // Добавляем сообщение со статусом Pending (серверного ID пока нет)
                ChatMessages.Add(new ChatMessage { Id = "pending", Sender = "Вы", Text = messageText, IsLocal = false });
                ChatListBox.ScrollIntoView(ChatMessages.Last());

                MessageTextBox.Text = "";
                ClearAttachedFile();
            }
            catch (Exception ex)
            {
                ShowSystemMessage($"Ошибка при отправке: {ex.Message}{Environment.NewLine}");
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
            ChatListBox.ItemsSource = ChatMessages;
            _voiceService.OnChatMessageReceived += OnChatMessageReceived;
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
                ShowSystemMessage("Соединение восстановлено");
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
            try
            {
                foreach (var message in historyData)
                {
                    string id = message.id?.ToString();
                    string sender = message.sender?.ToString();
                    string text = message.text?.ToString();

                    if (string.IsNullOrEmpty(sender) || string.IsNullOrEmpty(text)) continue;

                    // Извлекаем чистый текст из "голосовой ответ|текст"
                    string cleanText = text;
                    if (text.Contains("ответ|"))
                    {
                        var responses = new List<string>();
                        foreach (var part in text.Split('⸵'))
                        {
                            if (part.Contains("|"))
                                responses.Add(part.Split('|')[1]);
                            else
                                responses.Add(part);
                        }
                        cleanText = string.Join(" ", responses);
                    }

                    ChatMessages.Add(new ChatMessage
                    {
                        Id = id,
                        Sender = sender,
                        Text = cleanText,
                        IsLocal = false // История всегда с сервера
                    });
                }
            }
            catch (Exception ex) { ShowSystemMessage($"Ошибка истории: {ex.Message}"); }
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
                    ShowSystemMessage($"Ошибка при отправке запроса: {ex.Message}");
                }
                catch (Exception ex)
                {
                    ShowSystemMessage($"Произошла ошибка: {ex.Message}");
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
                ShowSystemMessage($"Ошибка при отключении устройства: {ex.Message}");
            }
        }

        private void AttachFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScreenshotButton.IsChecked == true)
            {
                ShowSystemMessage("Нельзя прикреплять файлы в режиме скриншота!");
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
                        ShowSystemMessage("Файл слишком большой! Максимальный размер: 10 МБ");
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
                    ShowSystemMessage($"Ошибка при загрузке файла: {ex.Message}");
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
                ShowSystemMessage($"Не удалось загрузить миниатюру: {ex.Message}");
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
                        ShowSystemMessage($"Ошибка: {responseObject.message}");
                    }
                    else
                    {
                        ShowAuthButtons();
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                ShowSystemMessage($"Ошибка при выходе из аккаунта: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowSystemMessage($"Произошла ошибка: {ex.Message}");
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

                ShowSystemMessage($"Добро пожаловать, {username}!");
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

        private void OnChatMessageReceived(ChatMessage msg)
        {
            Dispatcher.Invoke(() =>
            {
                ChatMessages.Add(msg);
                ChatListBox.ScrollIntoView(msg);
            });
        }
        public void ShowSystemMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var notificationService = new NotificationService();
                    notificationService.SendNotification(message);
                }
                catch { }
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
            }
            else
            {
                _voiceService.ListeningState.StartListening();
                _voiceService.StartListening();
                UpdateMicrophoneIcon(true);
            }
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

            // Метод UpdateSettingManager(_settingManager) больше не нужен

            MessageBox.Show("Настройки успешно обновлены!", "Успех!", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            ClearHistory();
        }

        public async void ClearHistory()
        {
            ChatMessages.Clear();
            ShowSystemMessage("История успешно очищена");
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
                        ShowSystemMessage($"Ошибка: {responseObject.message}");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                ShowSystemMessage($"Ошибка при отправке запроса: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowSystemMessage($"Произошла ошибка: {ex.Message}");
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
                    ShowSystemMessage("Режим скриншота активирован. Прикрепленный файл удален.");
                    ClearAttachedFile();
                }
            }
            else
            {
                _voiceService.IsScreenshotEnabled = false;
            }
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
            // Убираем заполнение текстового бокса при клике, чтобы не мешало редактированию
            // if (ChatListBox.SelectedItem != null) fillMessageTextBox(ChatListBox.SelectedItem.ToString());
        }

        public void fillMessageTextBox(string selectedText)
        {
            if (string.IsNullOrWhiteSpace(selectedText)) return;

            selectedText = new string(selectedText.Where(c => !char.IsControl(c)).ToArray()).Replace("Вы:", "").Replace("Бот:", "");
            if (!string.IsNullOrEmpty(selectedText))
            {
                MessageTextBox.Text = selectedText;
            }
        }

        // ЛОКАЛЬНОЕ УДАЛЕНИЕ
        private void DeleteChatMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ChatMessage msg)
            {
                if (!msg.IsLocal)
                {
                    MessageBox.Show("Функционал удаления для серверных сообщений будет добавлен позже.", "В разработке", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ChatMessages.Remove(msg);
            }
        }

        private void EditChatMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ChatMessage msg)
            {
                if (!msg.IsLocal)
                {
                    MessageBox.Show("Функционал редактирования для серверных сообщений будет добавлен позже.", "В разработке", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MessageTextBox.Text = msg.Text;
                _editingMessage = msg;
                SendMessageButton.Content = "Сохранить";
            }
        }


        private void CopyChatMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ChatMessage msg)
            {
                // Копирование текста можно оставить доступным и для серверных сообщений, 
                // но если вы хотите заблокировать и его, раскомментируйте код ниже:

                /*
                if (!msg.IsLocal)
                {
                    MessageBox.Show("Функционал копирования для серверных сообщений будет добавлен позже.", "В разработке", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                */

                Clipboard.SetText(msg.Text);
                ShowSystemMessage("Текст скопирован в буфер обмена");
            }
        }
        private async void RegenerateChatMessage_Click(object sender, RoutedEventArgs e)
        {
            
        }

        public void AttachFileFromScreenshot(AttachedFile file)
        {
            // Очищаем предыдущие прикрепления
            ClearAttachedFile();

            _attachedFile = file;
            _voiceService.AttachedFile = _attachedFile; // Передаем файл в VoiceService

            // Отображаем миниатюру
            try
            {
                using (var ms = new MemoryStream(file.Data))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // Создаем UI элементы для миниатюры (аналогично DisplayImageThumbnail)
                    // Этот код можно вынести в отдельный метод, если он повторяется
                    Border thumbnailContainer = new Border { /* ... стили ... */ Width = 160, Height = 160, Margin = new Thickness(5), CornerRadius = new CornerRadius(5) };
                    Grid grid = new Grid();
                    Image thumbnail = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = 150, MaxHeight = 150 };
                    Button closeButton = new Button { Content = "×", Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                    closeButton.Click += (s, e) => ClearAttachedFile();
                    grid.Children.Add(thumbnail);
                    grid.Children.Add(closeButton);
                    thumbnailContainer.Child = grid;
                    ThumbnailContainer.Items.Add(thumbnailContainer);
                }
            }
            catch (Exception ex)
            {
                ShowSystemMessage($"Не удалось отобразить скриншот: {ex.Message}");
            }
        }
    }

    public enum InputMode
    {
        NameResponseCommand,
        NamePlusCommand,
        Conversation
    }

    // --- КЛАССЫ КОНВЕРТЕРОВ ДЛЯ ВИЗУАЛА ЧАТА ---

    public class MessageAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text && text.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase))
                return HorizontalAlignment.Right;
            return HorizontalAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class MessageBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Фиолетовый для пользователя, стандартный серый для бота
            if (value is string text && text.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6A3D8B"));
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#625B71"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class MessageTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                if (text.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase))
                    return text.Substring(3).Trim();
                if (text.StartsWith("Бот:", StringComparison.OrdinalIgnoreCase))
                    return text.Substring(4).Trim();
                return text;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}