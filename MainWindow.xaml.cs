using Friday.Managers;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using System.Globalization;
using System.Security.Cryptography;

namespace Friday
{
    public class ChatMessage : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Sender { get; set; }
        public string Text { get; set; }

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
                if (long.TryParse(_editingMessage.Id, out long serverMsgId))
                {
                    try
                    {
                        var requestData = new
                        {
                            msg_id = serverMsgId,
                            new_text = messageText,
                            mac = GetMacAddress()
                        };

                        using (var client = new HttpClient())
                        {
                            var json = JsonConvert.SerializeObject(requestData);
                            var content = new StringContent(json, Encoding.UTF8, "application/json");
                            var response = await client.PostAsync("https://friday-assistant.ru/edit_message", content);

                            if (response.IsSuccessStatusCode)
                            {
                                _editingMessage.Text = messageText;
                                _editingMessage.DisplayText = messageText;
                            }
                            else
                            {
                                ShowSystemMessage("Ошибка при сохранении сообщения на сервере.");
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowSystemMessage($"Ошибка сети при редактировании: {ex.Message}");
                        return;
                    }
                }
                else
                {
                    _editingMessage.Text = messageText;
                    _editingMessage.DisplayText = messageText;
                }

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

                // 2. ПРОВЕРКА СКРИНШОТА ИЛИ ФОТО
                if (_attachedFile != null)
                {
                    screenshotBase64 = Convert.ToBase64String(_attachedFile.Data);
                }

                var message = new
                {
                    type = "текстовое сообщение",
                    command = messageText,
                    mac = GetMacAddress(),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    name = SettingManager.Setting.AssistantName,
                    voice_type = SettingManager.Setting.VoiceType, // 1. ПЕРЕДАЕМ ВЫБРАННЫЙ ГОЛОС
                    screenshot = screenshotBase64                  // 2. ПЕРЕДАЕМ ФОТО
                };

                ((App)Application.Current).SendWebSocketMessage(message);

                ChatMessages.Add(new ChatMessage { Id = "pending", Sender = "Вы", Text = messageText });
                ChatListBox.UpdateLayout();
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
                                // Получаем уникальный GUID системы
                                string machineGuid = value.ToString().Replace("-", "").ToUpper();

                                // Хешируем его, чтобы получить фиксированную длину
                                using (MD5 md5 = MD5.Create())
                                {
                                    byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(machineGuid));
                                    StringBuilder sb = new StringBuilder();
                                    // Берем первые 6 байт хеша и собираем аналог MAC-адреса с тире
                                    for (int i = 0; i < 6; i++)
                                    {
                                        sb.Append(hashBytes[i].ToString("X2"));
                                        if (i < 5) sb.Append("-");
                                    }
                                    return sb.ToString(); // Вернет стабильный XX-XX-XX-XX-XX-XX
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

            // Резервный вариант, если чтение реестра заблокировано политиками безопасности
            return "00-11-22-33-44-55";
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
                    });
                }
            }
            catch (Exception ex) { ShowSystemMessage($"Ошибка истории: {ex.Message}"); }
        }


        private void AnimateTabContent()
        {
            if (MainTabControl?.Template?.FindName("ContentHost", MainTabControl) is FrameworkElement host)
            {
                var tt = new System.Windows.Media.TranslateTransform(0, 12);
                host.RenderTransform = tt;

                host.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25))
                    {
                        EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    });

                tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(12, 0, TimeSpan.FromSeconds(0.3))
                    {
                        EasingFunction = new System.Windows.Media.Animation.CubicEase
                            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    });
            }
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            // Only animate on actual tab changes, not inner ComboBox/ListBox changes
            if (e.AddedItems[0] is TabItem) AnimateTabContent();

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
                ChatListBox.UpdateLayout();
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


        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
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
            string voiceType = (VoiceTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            int volume = Convert.ToInt32(VolumeSlider.Value);
            string inputMode = (InputModeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();

            if (string.IsNullOrEmpty(assistantName) || string.IsNullOrEmpty(voiceType) || string.IsNullOrEmpty(inputMode))
            {
                MessageBox.Show("Поля не могут быть пустыми", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

        // УДАЛЕНИЕ СООБЩЕНИЯ
        private async void DeleteChatMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ChatMessage msg)
            {
                // Если ID числовой — значит это сообщение из БД
                if (long.TryParse(msg.Id, out long serverMsgId))
                {
                    try
                    {
                        var requestData = new
                        {
                            msg_id = serverMsgId,
                            mac = GetMacAddress()
                        };

                        using (var client = new HttpClient())
                        {
                            var json = JsonConvert.SerializeObject(requestData);
                            var content = new StringContent(json, Encoding.UTF8, "application/json");

                            var response = await client.PostAsync("https://friday-assistant.ru/delete_message", content);

                            if (response.IsSuccessStatusCode)
                            {
                                ChatMessages.Remove(msg);
                            }
                            else
                            {
                                ShowSystemMessage("Ошибка при удалении сообщения с сервера.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowSystemMessage($"Ошибка сети при удалении: {ex.Message}");
                    }
                }
                else
                {
                    // Если локальное (Guid или pending) — просто удаляем из списка
                    ChatMessages.Remove(msg);
                }
            }
        }

        // РЕДАКТИРОВАНИЕ СООБЩЕНИЯ
        private void EditChatMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ChatMessage msg)
            {
                // Убрали проверку IsLocal! Теперь редактировать можно всё.
                MessageTextBox.Text = msg.Text;
                _editingMessage = msg;
                SendMessageButton.Content = "Сохранить";
            }
        }

        // КОПИРОВАНИЕ СООБЩЕНИЯ (Оставляем как есть, оно работало отлично)
        private void CopyChatMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ChatMessage msg)
            {
                Clipboard.SetText(msg.Text);
                ShowSystemMessage("Текст скопирован в буфер обмена");
            }
        }
        private async void RegenerateChatMessage_Click(object sender, RoutedEventArgs e)
        {
            
        }

        public void ConfirmPendingMessage(string realId)
        {
            if (string.IsNullOrEmpty(realId)) return;

            // Ищем последнее сообщение пользователя со статусом pending
            var pendingMsg = ChatMessages.LastOrDefault(m => m.IsUser && m.Id == "pending");
            if (pendingMsg != null)
            {
                pendingMsg.Id = realId; // Заменяем "pending" на настоящий ID из базы!
            }
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