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
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Threading;

// ПОДКЛЮЧАЕМ AFORGE ДЛЯ КАМЕРЫ
using AForge.Video;
using AForge.Video.DirectShow;

namespace Friday
{
    public class ChatMessage : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string UiMsgId { get; set; }
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
        private int _editingMessageIndex = -1;
        public ObservableCollection<ChatMessage> ChatMessages { get; set; } = new ObservableCollection<ChatMessage>();
        private ChatMessage _editingMessage = null;

        public ObservableCollection<DialogItem> Dialogs { get; set; } = new ObservableCollection<DialogItem>();
        public long? CurrentDialogId { get; private set; } = null;
        private bool _suppressDialogSelection = false;
        private double _dialogsPanelWidth = 300;

        private bool IsGuest => !((App)Application.Current).IsLoggedIn;
        private string AccountToken => ((App)Application.Current).AccountToken;

        public AttachedFile GetAttachedFile() => _attachedFile;
        private dynamic _userData;

        // ПУТЬ ДЛЯ ИСТОРИИ ГОСТЯ
        private readonly string _guestHistoryPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\guest_history.json"));

        // ПЕРЕМЕННЫЕ ДЛЯ ВИДЕОСТРИМА
        private VideoCaptureDevice _videoSource;
        private Bitmap _currentVideoFrame;
        private readonly object _videoLock = new object();
        private DispatcherTimer _videoStreamTimer;
        private DispatcherTimer _screenCaptureTimer;

        public MainWindow(dynamic responseData = null)
        {
            InitializeComponent();
            _userData = responseData;

            // Инициализация таймера отправки кадров на сервер (1 раз в секунду)
            _videoStreamTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _videoStreamTimer.Tick += VideoStreamTimer_Tick;

            InitializeUserInterface();
            LoadSettings();
            UpdateMicrophoneIcon(false);

            _voiceService = new VoiceService(_settingManager, this);
            _settingManager.SettingsChanged += SettingManager_SettingsChanged;

            ((App)Application.Current).VoiceService = _voiceService;
            ((App)Application.Current).IncrementWindowCount();
            ChatListBox.ItemsSource = ChatMessages;
            DialogListBox.ItemsSource = Dialogs;
            _voiceService.OnChatMessageReceived += OnChatMessageReceived;

            DataContext = this;

            if (IsGuest)
            {
                ApplyGuestMode();
                LoadGuestHistory(); // Загружаем историю из файла для гостя
            }
            else ApplyAccountMode();
        }

        // ================= ЛОГИКА ИСТОРИИ ГОСТЯ =================
        private void SaveGuestHistory()
        {
            if (!IsGuest) return;
            try
            {
                string dir = Path.GetDirectoryName(_guestHistoryPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(ChatMessages);
                File.WriteAllText(_guestHistoryPath, json);
            }
            catch (Exception ex) { Console.WriteLine("Ошибка сохранения истории: " + ex.Message); }
        }

        private void LoadGuestHistory()
        {
            if (!IsGuest) return;
            try
            {
                if (File.Exists(_guestHistoryPath))
                {
                    var json = File.ReadAllText(_guestHistoryPath);
                    var msgs = JsonConvert.DeserializeObject<ObservableCollection<ChatMessage>>(json);
                    if (msgs != null)
                    {
                        ChatMessages.Clear();
                        foreach (var m in msgs) ChatMessages.Add(m);
                        if (ChatMessages.Count > 0)
                        {
                            ChatListBox.UpdateLayout();
                            ChatListBox.ScrollIntoView(ChatMessages.Last());
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Ошибка загрузки истории: " + ex.Message); }
        }

        public List<object> GetGuestMessageHistory()
        {
            var history = new List<object>();
            var recentMessages = ChatMessages
                .Where(m => !string.IsNullOrEmpty(m.Text) && !m.Text.Contains("⏳") && !m.Text.Contains("🎤"))
                .Reverse().Take(10).Reverse();
            foreach (var msg in recentMessages)
            {
                history.Add(new { role = msg.IsUser ? "user" : "assistant", content = msg.Text });
            }
            return history;
        }

        // ================= ЛОГИКА КАМЕРЫ И ЭКРАНА =================
        private void CameraBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CameraBtn.IsChecked == true)
            {
                ScreenBtn.IsChecked = false;
                StopVideo();
                StartCamera();
            }
            else StopVideo();
        }

        private void ScreenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ScreenBtn.IsChecked == true)
            {
                CameraBtn.IsChecked = false;
                StopVideo();
                StartScreenCapture();
            }
            else StopVideo();
        }

        private void StartCamera()
        {
            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (videoDevices.Count == 0)
            {
                ShowSystemMessage("Камера не найдена.");
                CameraBtn.IsChecked = false;
                return;
            }

            _videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
            _videoSource.NewFrame += (s, ev) =>
            {
                lock (_videoLock)
                {
                    _currentVideoFrame?.Dispose();
                    _currentVideoFrame = (Bitmap)ev.Frame.Clone();
                    _currentVideoFrame.RotateFlip(RotateFlipType.RotateNoneFlipX);
                }

                // Рендерим картинку в UI ТОЛЬКО если превью открыто
                if (VideoPreviewContainer.Visibility == Visibility.Visible)
                {
                    Dispatcher.Invoke(() => {
                        VideoPreviewImage.Source = BitmapToImageSource(_currentVideoFrame);
                    });
                }
            };

            _videoSource.Start();
            VideoPreviewContainer.Visibility = Visibility.Visible;
            _videoStreamTimer.Start();
        }

        private void StartScreenCapture()
        {
            // Таймер превью для экрана (10 FPS)
            _screenCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _screenCaptureTimer.Tick += (s, e) =>
            {
                if (VideoPreviewContainer.Visibility == Visibility.Visible)
                {
                    try
                    {
                        var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                        var bmp = new Bitmap(bounds.Width, bounds.Height);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                        }
                        VideoPreviewImage.Source = BitmapToImageSource(bmp);
                        bmp.Dispose();
                    }
                    catch { }
                }
            };

            _screenCaptureTimer.Start();
            VideoPreviewContainer.Visibility = Visibility.Visible;
            _videoStreamTimer.Start();
        }

        // КРЕСТИК: Выключает ТОЛЬКО локальное превью и глушит таймер перерисовки
        private void CloseVideoPreview_Click(object sender, RoutedEventArgs e)
        {
            VideoPreviewContainer.Visibility = Visibility.Collapsed;
            _screenCaptureTimer?.Stop(); // Полностью снимаем нагрузку на процессор при стриминге экрана
        }

        // ПОЛНАЯ ОСТАНОВКА (только если отжали саму кнопку "Экран" или "Камера")
        private void StopVideo()
        {
            _videoStreamTimer?.Stop();
            _screenCaptureTimer?.Stop();

            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                _videoSource.WaitForStop();
                _videoSource = null;
            }

            VideoPreviewContainer.Visibility = Visibility.Collapsed;

            lock (_videoLock)
            {
                _currentVideoFrame?.Dispose();
                _currentVideoFrame = null;
            }
        }

        // Получение кадра для ИИ: если экран активен, фоткаем его ровно в момент запроса
        private string GetCurrentVideoFrameBase64()
        {
            // 1. Если включен захват ЭКРАНА: делаем скриншот на лету без предварительного рендера
            if (ScreenBtn.IsChecked == true)
            {
                try
                {
                    var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                    using (var bmp = new Bitmap(bounds.Width, bounds.Height))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                        }
                        using (var ms = new MemoryStream())
                        {
                            bmp.Save(ms, ImageFormat.Jpeg);
                            return Convert.ToBase64String(ms.ToArray());
                        }
                    }
                }
                catch { return null; }
            }

            // 2. Если включена КАМЕРА: берем последний кадр из памяти
            if (CameraBtn.IsChecked == true)
            {
                lock (_videoLock)
                {
                    if (_currentVideoFrame != null)
                    {
                        using (var ms = new MemoryStream())
                        {
                            _currentVideoFrame.Save(ms, ImageFormat.Jpeg);
                            return Convert.ToBase64String(ms.ToArray());
                        }
                    }
                }
            }

            return null;
        }

        // Таймер отправки кадров во время голоса (1 раз в секунду)
        private void VideoStreamTimer_Tick(object sender, EventArgs e)
        {
            var vs = ((App)Application.Current).VoiceService;
            if (vs != null && vs.IsRecordingCommand && !string.IsNullOrEmpty(vs.CurrentCommandMsgId))
            {
                string base64 = GetCurrentVideoFrameBase64();
                if (base64 != null)
                {
                    ((App)Application.Current).SendWebSocketMessage(new
                    {
                        type = "video_stream_chunk",
                        ui_msg_id = vs.CurrentCommandMsgId,
                        video_base64 = base64
                    });
                }
            }
        }
        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Jpeg);
                memory.Position = 0;
                BitmapImage bitmapimage = new BitmapImage();
                bitmapimage.BeginInit();
                bitmapimage.StreamSource = memory;
                bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapimage.EndInit();
                bitmapimage.Freeze(); // Необходимо для передачи между потоками
                return bitmapimage;
            }
        }

        // ================= ОТПРАВКА СООБЩЕНИЙ =================
        private async void SendMessageButton_Click(object sender, RoutedEventArgs e) => await SendCurrentMessageAsync();

        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SendCurrentMessageAsync().ConfigureAwait(false);
            }
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

            if (_editingMessage != null)
            {
                if (long.TryParse(_editingMessage.Id, out long serverMsgId))
                {
                    try
                    {
                        object requestData = IsGuest
                            ? (object)new { msg_id = serverMsgId, new_text = messageText, mac = GetMacAddress() }
                            : (object)new { msg_id = serverMsgId, new_text = messageText, token = AccountToken };
                        using (var client = new HttpClient())
                        {
                            var json = JsonConvert.SerializeObject(requestData);
                            var content = new StringContent(json, Encoding.UTF8, "application/json");
                            var response = await client.PostAsync("https://friday-assistant.ru/edit_message", content);
                            if (response.IsSuccessStatusCode)
                            {
                                _editingMessage.Text = messageText;
                                _editingMessage.DisplayText = messageText;
                                SaveGuestHistory();
                            }
                            else { ShowSystemMessage("Ошибка при сохранении сообщения на сервере."); return; }
                        }
                    }
                    catch (Exception ex) { ShowSystemMessage($"Ошибка сети при редактировании: {ex.Message}"); return; }
                }
                else
                {
                    _editingMessage.Text = messageText;
                    _editingMessage.DisplayText = messageText;
                    SaveGuestHistory();
                }

                _editingMessage = null;
                SendMessageButton.Content = "Отправить";
                MessageTextBox.Text = "";
                return;
            }

            var app = (App)Application.Current;
            if (app.VoiceService != null && (bool)app.VoiceService.GetType().GetField("_isWaitingForServer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(app.VoiceService))
            {
                ShowSystemMessage("Ожидаю ответа от сервера. Пожалуйста, подождите...");
                return;
            }

            try
            {
                // Если прикреплен файл - берем его. Если нет, но включена камера/экран - берем скриншот
                string screenshotBase64 = _attachedFile != null ? Convert.ToBase64String(_attachedFile.Data) : GetCurrentVideoFrameBase64();
                string pendingId = Guid.NewGuid().ToString();
                bool isGuest = IsGuest;

                var message = new
                {
                    type = "текстовое сообщение",
                    command = messageText,
                    mac = GetMacAddress(),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    voice_type = SettingManager.Setting.VoiceType,
                    screenshot = screenshotBase64,
                    ui_msg_id = pendingId,
                    dialog_id = isGuest ? (long?)null : CurrentDialogId,
                    message_history = isGuest ? GetGuestMessageHistory() : null
                };

                ((App)Application.Current).VoiceService?.SetWaitingForServer();
                ((App)Application.Current).SendWebSocketMessage(message);

                ChatMessages.Add(new ChatMessage { Id = pendingId, UiMsgId = pendingId, Sender = "Вы", Text = messageText });
                ChatListBox.UpdateLayout();
                ChatListBox.ScrollIntoView(ChatMessages.Last());

                SaveGuestHistory(); // Сохраняем сообщение гостя

                MessageTextBox.Text = "";
                ClearAttachedFile();
            }
            catch (Exception ex)
            {
                ShowSystemMessage($"Ошибка при отправке: {ex.Message}{Environment.NewLine}");
            }
        }

        // ================= ОСТАЛЬНОЙ КОД БЕЗ ИЗМЕНЕНИЙ =================
        public void UpdateData(dynamic responseData)
        {
            if (responseData != null) ShowSystemMessage("Соединение восстановлено");
        }

        private void SettingManager_SettingsChanged(object sender, SettingChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(e.VoiceType))
                {
                    foreach (ComboBoxItem item in VoiceTypeComboBox.Items)
                    {
                        if (item.Content.ToString() == e.VoiceType) { VoiceTypeComboBox.SelectedItem = item; break; }
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

                    string cleanText = text;
                    if (text.Contains("ответ|"))
                    {
                        var responses = new List<string>();
                        foreach (var part in text.Split('⸵'))
                        {
                            if (part.Contains("|")) responses.Add(part.Split('|')[1]);
                            else responses.Add(part);
                        }
                        cleanText = string.Join(" ", responses);
                    }
                    ChatMessages.Add(new ChatMessage { Id = id, Sender = sender, Text = cleanText });
                }
                SaveGuestHistory();
            }
            catch (Exception ex) { ShowSystemMessage($"Ошибка истории: {ex.Message}"); }
        }

        private void AnimateTabContent()
        {
            if (MainTabControl?.Template?.FindName("ContentHost", MainTabControl) is FrameworkElement host)
            {
                var tt = new System.Windows.Media.TranslateTransform(0, 12);
                host.RenderTransform = tt;
                host.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
                tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(12, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
            }
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            if (e.AddedItems[0] is TabItem) AnimateTabContent();
            if (e.AddedItems[0] is TabItem selectedTab && selectedTab.Header.ToString() == "Устройства")
            {
                try
                {
                    var message = new { mac = GetMacAddress() };
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
                            else { AccountDevicesList.ItemsSource = null; NoAccountDevicesText.Visibility = Visibility.Visible; }

                            if (responseObject.my_devices != null && responseObject.my_devices.Count > 0)
                            {
                                ConnectedDevicesList.ItemsSource = responseObject.my_devices;
                                NoConnectedDevicesText.Visibility = Visibility.Collapsed;
                            }
                            else { ConnectedDevicesList.ItemsSource = null; NoConnectedDevicesText.Visibility = Visibility.Visible; }
                        });
                    }
                }
                catch (Exception ex) { ShowSystemMessage($"Произошла ошибка: {ex.Message}"); }
            }
        }

        private async void DisconnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button)) return;
            string targetMacAddress = button.Tag as string;
            if (string.IsNullOrEmpty(targetMacAddress)) return;

            try
            {
                var message = new { requester_mac = GetMacAddress(), target_mac = targetMacAddress };
                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(message);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync("https://friday-assistant.ru/disconnect_device", content);
                    response.EnsureSuccessStatusCode();
                    var devicesTab = (TabItem)this.FindName("DevicesTab");
                    TabControl_SelectionChanged(null, new SelectionChangedEventArgs(TabControl.SelectionChangedEvent, new List<object>(), new List<object> { devicesTab }));
                }
            }
            catch (Exception ex) { ShowSystemMessage($"Ошибка при отключении устройства: {ex.Message}"); }
        }

        private void AttachFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Image Files (*.jpg; *.jpeg; *.png; *.bmp)|*.jpg; *.jpeg; *.png; *.bmp|All Files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    ClearAttachedFile();
                    string filePath = openFileDialog.FileName;
                    string fileName = Path.GetFileName(filePath);
                    long fileSize = new FileInfo(filePath).Length;

                    if (fileSize > 10 * 1024 * 1024) { ShowSystemMessage("Файл слишком большой! Максимальный размер: 10 МБ"); return; }

                    _attachedFile = new AttachedFile { Name = fileName, Data = File.ReadAllBytes(filePath), Size = fileSize };
                    if (IsImageFile(fileName)) DisplayImageThumbnail(filePath);
                    _voiceService.AttachedFile = _attachedFile;
                }
                catch (Exception ex) { ShowSystemMessage($"Ошибка при загрузке файла: {ex.Message}"); }
            }
        }

        private bool IsImageFile(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp";
        }

        private void DisplayImageThumbnail(string imagePath)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.UriSource = new Uri(imagePath); bitmap.DecodePixelWidth = 150; bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();

                // ИСПРАВЛЕНО: Явное указание System.Windows.Media.Brushes
                Border thumbnailContainer = new Border { Background = System.Windows.Media.Brushes.Transparent, BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), CornerRadius = new CornerRadius(5), Width = 160, Height = 160 };
                Grid grid = new Grid();
                System.Windows.Controls.Image thumbnail = new System.Windows.Controls.Image { Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 150, MaxHeight = 150 };

                // ИСПРАВЛЕНО: Явное указание System.Windows.Media.Brushes
                Button closeButton = new Button { Content = "×", Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0), Padding = new Thickness(0), FontWeight = FontWeights.Bold, Background = System.Windows.Media.Brushes.White, Foreground = System.Windows.Media.Brushes.Black, BorderThickness = new Thickness(0) };

                closeButton.Click += (s, e) => ClearAttachedFile();
                grid.Children.Add(thumbnail); grid.Children.Add(closeButton); thumbnailContainer.Child = grid;
                ThumbnailContainer.Items.Add(thumbnailContainer);
            }
            catch (Exception ex) { ShowSystemMessage($"Не удалось загрузить миниатюру: {ex.Message}"); }
        }

        public class AttachedFile { public string Name { get; set; } public byte[] Data { get; set; } public long Size { get; set; } }
        public class DeviceResponse { public List<DeviceInfo> account_devices { get; set; } public List<DeviceInfo> my_devices { get; set; } public string status { get; set; } }
        public class DeviceInfo { public string DeviceName { get; set; } public string MacAddress { get; set; } public bool IsOnline { get; set; } public bool IsAccountDevice { get; set; } }

        private void ConnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            var connectDeviceWindow = new ConnectDeviceWindow();
            if (connectDeviceWindow.ShowDialog() == true) TabControl_SelectionChanged(null, new SelectionChangedEventArgs(TabControl.SelectionChangedEvent, new List<object>(), new List<object> { DevicesTab }));
        }

        private void InitializeUserInterface()
        {
            if (_userData != null && _userData.user_login != null && !string.IsNullOrEmpty(_userData.user_login.ToString())) ShowUserButton(_userData.user_login.ToString());
            else ShowAuthButtons();
            if (_userData != null && _userData.history != null) ProcessHistoryMessages(_userData.history);
        }

        public void ShowUserButton(string username)
        {
            UserButtonText.Text = username;
            UserButton.Visibility = Visibility.Visible;
            LoginButton.Visibility = Visibility.Collapsed;
            RegisterButton.Visibility = Visibility.Collapsed;
            ApplyAccountMode();
        }

        public void ShowAuthButtons()
        {
            UserButton.Visibility = Visibility.Collapsed;
            LoginButton.Visibility = Visibility.Visible;
            RegisterButton.Visibility = Visibility.Visible;
            ApplyGuestMode();
        }

        public void ApplyAccountMode()
        {
            Dispatcher.Invoke(() =>
            {
                DialogsPanel.Visibility = Visibility.Visible;
                DialogsSplitter.Visibility = Visibility.Visible;
                DialogsColumn.Width = new GridLength(_dialogsPanelWidth);
                ClearHistoryButton.Visibility = Visibility.Collapsed;
            });
        }

        public void ApplyGuestMode()
        {
            Dispatcher.Invoke(() =>
            {
                if (DialogsPanel.Visibility == Visibility.Visible && DialogsColumn.ActualWidth > 1)
                    _dialogsPanelWidth = DialogsColumn.ActualWidth;

                DialogsPanel.Visibility = Visibility.Collapsed;
                DialogsSplitter.Visibility = Visibility.Collapsed;
                DialogsColumn.Width = new GridLength(0);
                ClearHistoryButton.Visibility = Visibility.Visible;
                Dialogs.Clear();
                CurrentDialogId = null;
                UpdateDialogsCount();
            });
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
                var logoutCommand = new { MAC = GetMacAddress(), Command = "logout" };
                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(logoutCommand);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync("https://friday-assistant.ru/logout", content);
                    response.EnsureSuccessStatusCode();

                    string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\account.json"));
                    if (File.Exists(filePath)) File.Delete(filePath);

                    ((App)Application.Current).ResetAccountData();
                    ShowAuthButtons();
                    ChatMessages.Clear();
                    SaveGuestHistory();
                    Dialogs.Clear();
                    CurrentDialogId = null;
                    ShowSystemMessage("Вы вышли из аккаунта. Включен гостевой режим.");
                }
            }
            catch (Exception ex) { ShowSystemMessage($"Произошла ошибка: {ex.Message}"); }
        }

        public void SyncAccountData(dynamic responseData)
        {
            string login = null;
            dynamic fallbackHistory = null;
            try
            {
                if (responseData != null && responseData.user_login != null) login = responseData.user_login.ToString();
                if (responseData != null && responseData.history != null) fallbackHistory = responseData.history;
            }
            catch { }

            Dispatcher.Invoke(() =>
            {
                ChatMessages.Clear();
                if (!string.IsNullOrEmpty(login)) ShowUserButton(login);
            });

            _ = SyncDialogsAfterLoginAsync(fallbackHistory);
        }

        private async Task SyncDialogsAfterLoginAsync(dynamic fallbackHistory)
        {
            try
            {
                await LoadDialogsAsync();

                if (Dialogs.Count > 0)
                {
                    await SelectDialogAsync(Dialogs[0].Id);
                }
                else if (fallbackHistory != null)
                {
                    Dispatcher.Invoke(() => ProcessHistoryMessages(fallbackHistory));
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Dialogs] sync: {ex.Message}"); }
        }

        private void UpdateDialogsCount()
        {
            if (DialogsCountText != null)
                DialogsCountText.Text = Dialogs.Count > 0 ? Dialogs.Count.ToString() : "";
        }

        public async Task LoadDialogsAsync()
        {
            if (IsGuest) { ApplyGuestMode(); return; }

            var list = await DialogService.GetDialogsAsync(AccountToken);

            Dispatcher.Invoke(() =>
            {
                _suppressDialogSelection = true;
                Dialogs.Clear();
                foreach (var d in list) Dialogs.Add(d);
                var active = Dialogs.FirstOrDefault(d => CurrentDialogId.HasValue && d.Id == CurrentDialogId.Value);
                DialogListBox.SelectedItem = active;
                _suppressDialogSelection = false;
                UpdateDialogsCount();
            });
        }

        public async Task SelectDialogAsync(long dialogId)
        {
            if (IsGuest) return;

            CurrentDialogId = dialogId;

            Dispatcher.Invoke(() =>
            {
                ChatMessages.Clear();
                _suppressDialogSelection = true;
                DialogListBox.SelectedItem = Dialogs.FirstOrDefault(d => d.Id == dialogId);
                _suppressDialogSelection = false;
            });

            var history = await DialogService.GetHistoryAsync(AccountToken, dialogId);

            Dispatcher.Invoke(() =>
            {
                foreach (var msg in history)
                {
                    if (string.IsNullOrEmpty(msg.Sender)) continue;

                    string text = msg.Sender == "Вы"
                        ? msg.Text
                        : DialogService.CleanBotText(msg.Text);

                    if (string.IsNullOrWhiteSpace(text)) continue;

                    ChatMessages.Add(new ChatMessage
                    {
                        Id = msg.Id.ToString(),
                        Sender = msg.Sender,
                        Text = text
                    });
                }

                if (ChatMessages.Count > 0)
                {
                    ChatListBox.UpdateLayout();
                    ChatListBox.ScrollIntoView(ChatMessages.Last());
                }
            });
        }

        private void NewChatButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsGuest) { new LoginWindow().ShowDialog(); return; }

            CurrentDialogId = null;
            ChatMessages.Clear();
            _suppressDialogSelection = true;
            DialogListBox.SelectedItem = null;
            _suppressDialogSelection = false;
            ShowSystemMessage("Новый чат. Напишите сообщение, чтобы начать.");
        }

        private async void DialogListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDialogSelection) return;
            if (DialogListBox.SelectedItem is DialogItem d && d.Id != CurrentDialogId)
                await SelectDialogAsync(d.Id);
        }

        private async void DeleteDialogButton_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.DataContext is DialogItem dlg)) return;

            var confirm = MessageBox.Show($"Удалить диалог «{dlg.Name}»?", "Подтверждение",
                                          MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            bool ok = await DialogService.DeleteDialogAsync(AccountToken, dlg.Id);
            if (!ok) { ShowSystemMessage("Не удалось удалить диалог."); return; }

            if (CurrentDialogId == dlg.Id)
            {
                CurrentDialogId = null;
                Dispatcher.Invoke(() => ChatMessages.Clear());
            }

            await LoadDialogsAsync();
            ShowSystemMessage("Диалог удалён.");
        }

        public async void OnDialogCreated(long dialogId, string name)
        {
            CurrentDialogId = dialogId;
            await LoadDialogsAsync();
        }

        public void OnDialogRenamed(long dialogId, string name)
        {
            Dispatcher.Invoke(() =>
            {
                var d = Dialogs.FirstOrDefault(x => x.Id == dialogId);
                if (d != null) d.Name = name;
            });
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e) => new LoginWindow().ShowDialog();
        private void RegisterButton_Click(object sender, RoutedEventArgs e) => new RegisterWindow().ShowDialog();

        public void UpdateAfterRegistration(string username)
        {
            Dispatcher.Invoke(() => { ShowUserButton(username); ShowSystemMessage($"Добро пожаловать, {username}!"); });
        }

        private void OnMessageReceived(string message) { Dispatcher.Invoke(() => { ChatListBox.Items.Add(message + Environment.NewLine); ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]); }); }
        private void OnChatMessageReceived(ChatMessage msg)
        {
            Dispatcher.Invoke(() => {
                ChatMessages.Add(msg);
                ChatListBox.UpdateLayout();
                ChatListBox.ScrollIntoView(msg);
                SaveGuestHistory();
            });
        }
        public void ShowSystemMessage(string message) { Dispatcher.Invoke(() => { try { new NotificationService().SendNotification(message); } catch { } }); }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized == WindowState ? WindowState.Normal : WindowState.Maximized;
        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

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

        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }

        private void LoadSettings()
        {
            foreach (ComboBoxItem item in VoiceTypeComboBox.Items)
            {
                if (item.Content.ToString() == SettingManager.Setting.VoiceType)
                {
                    VoiceTypeComboBox.SelectedItem = item;
                    break;
                }
            }
            MusicFolderPathTextBox.Text = SettingManager.Setting.MusicFolderPath;
        }

        private void ChooseMusicFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Выберите папку с музыкой", InitialDirectory = Directory.Exists(MusicFolderPathTextBox.Text) ? MusicFolderPathTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) };
            if (dialog.ShowDialog() == true) MusicFolderPathTextBox.Text = dialog.FolderName;
        }

        public void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            string voiceType = (VoiceTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            string musicFolder = MusicFolderPathTextBox.Text;

            if (string.IsNullOrEmpty(voiceType))
            {
                MessageBox.Show("Поля не могут быть пустыми", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settingManager.UpdateSettings(voiceType, musicFolder);
            MessageBox.Show("Настройки успешно обновлены!", "Успех!", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ClearHistoryButton_Click(object sender, RoutedEventArgs e) => ClearHistory();

        public void ClearHistory()
        {
            if (!IsGuest)
            {
                ShowSystemMessage("Чтобы очистить историю, удалите диалог в списке слева.");
                return;
            }

            Dispatcher.Invoke(() => {
                ChatMessages.Clear();
                SaveGuestHistory();
            });
            ShowSystemMessage("История успешно очищена");
        }

        public void ManageAppsButton_Click(object sender, RoutedEventArgs e) => new Friday.Windows.AppPathsWindow().ShowDialog();
        public void ClearAttachedFile() { _attachedFile = null; ThumbnailContainer.Items.Clear(); }
        public void ChangedataButton_Click(object sender, RoutedEventArgs e) => new ChangeDataWindow().Show();

        protected override void OnClosed(EventArgs e)
        {
            _settingManager.SettingsChanged -= SettingManager_SettingsChanged;
            StopVideo(); // Выключаем камеру при закрытии
            ((App)Application.Current).DecrementWindowCount();
            base.OnClosed(e);
        }

        private void ChatListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        public void fillMessageTextBox(string selectedText) { if (!string.IsNullOrWhiteSpace(selectedText)) MessageTextBox.Text = new string(selectedText.Where(c => !char.IsControl(c)).ToArray()).Replace("Вы:", "").Replace("Бот:", ""); }

        private async void DeleteChatMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ChatMessage msg)
            {
                if (long.TryParse(msg.Id, out long serverMsgId))
                {
                    try
                    {
                        object requestData = IsGuest
                            ? (object)new { msg_id = serverMsgId, mac = GetMacAddress() }
                            : (object)new { msg_id = serverMsgId, token = AccountToken };
                        using (var client = new HttpClient())
                        {
                            var json = JsonConvert.SerializeObject(requestData);
                            var content = new StringContent(json, Encoding.UTF8, "application/json");
                            var response = await client.PostAsync("https://friday-assistant.ru/delete_message", content);
                            if (response.IsSuccessStatusCode)
                            {
                                ChatMessages.Remove(msg);
                                SaveGuestHistory();
                            }
                            else ShowSystemMessage("Ошибка при удалении сообщения с сервера.");
                        }
                    }
                    catch (Exception ex) { ShowSystemMessage($"Ошибка сети при удалении: {ex.Message}"); }
                }
                else
                {
                    ChatMessages.Remove(msg);
                    SaveGuestHistory();
                }
            }
        }

        private void EditChatMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ChatMessage msg) { MessageTextBox.Text = msg.Text; _editingMessage = msg; SendMessageButton.Content = "Сохранить"; }
        }

        private void CopyChatMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ChatMessage msg) { Clipboard.SetText(msg.Text); ShowSystemMessage("Текст скопирован в буфер обмена"); }
        }

        private async void RegenerateChatMessage_Click(object sender, RoutedEventArgs e) { }

        public void AttachFileFromScreenshot(AttachedFile file)
        {
            ClearAttachedFile();
            _attachedFile = file; _voiceService.AttachedFile = _attachedFile;
            try
            {
                using (var ms = new MemoryStream(file.Data))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = ms; bitmap.EndInit(); bitmap.Freeze();
                    Border thumbnailContainer = new Border { Width = 160, Height = 160, Margin = new Thickness(5), CornerRadius = new CornerRadius(5) };
                    Grid grid = new Grid();
                    System.Windows.Controls.Image thumbnail = new System.Windows.Controls.Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = 150, MaxHeight = 150 };
                    Button closeButton = new Button { Content = "×", Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                    closeButton.Click += (s, ev) => ClearAttachedFile();
                    grid.Children.Add(thumbnail); grid.Children.Add(closeButton); thumbnailContainer.Child = grid;
                    ThumbnailContainer.Items.Add(thumbnailContainer);
                }
            }
            catch (Exception ex) { ShowSystemMessage($"Не удалось отобразить скриншот: {ex.Message}"); }
        }
        // ================= ДЛЯ ТЯЖЕЛОГО МОЗГА =================
        public (string Base64, int Width, int Height) CaptureScreenForAI()
        {
            try
            {
                var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using (var bmp = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                    }

                    // Сжимаем скриншот, чтобы не жрать лимиты ИИ и вебсокета
                    int targetWidth = bounds.Width;
                    int targetHeight = bounds.Height;
                    if (targetWidth > 1920)
                    {
                        targetHeight = (int)(targetHeight * (1920.0 / targetWidth));
                        targetWidth = 1920;
                    }

                    using (var resizedBmp = new Bitmap(bmp, targetWidth, targetHeight))
                    using (var ms = new MemoryStream())
                    {
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 50L);
                        var jpegCodec = ImageCodecInfo.GetImageDecoders().FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

                        resizedBmp.Save(ms, jpegCodec, encoderParams);
                        return (Convert.ToBase64String(ms.ToArray()), bounds.Width, bounds.Height);
                    }
                }
            }
            catch { return (null, 0, 0); }
        }
    }

    public class MessageAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return value is string text && text.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase) ? HorizontalAlignment.Right : HorizontalAlignment.Left; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class MessageBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ИСПРАВЛЕНО: Явное указание System.Windows.Media.Color и ColorConverter
            return value is string text && text.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6A3D8B"))
                : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#625B71"));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class MessageTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                if (text.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase)) return text.Substring(3).Trim();
                if (text.StartsWith("Бот:", StringComparison.OrdinalIgnoreCase)) return text.Substring(4).Trim();
                return text;
            }
            return value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }


}