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

        // ====================== ДИАЛОГИ (аккаунт) ======================
        /// <summary>Список диалогов в левой панели. Аналог #dialogList из веба.</summary>
        public ObservableCollection<DialogItem> Dialogs { get; set; } = new ObservableCollection<DialogItem>();

        /// <summary>
        /// Текущий диалог. null = "Новый чат": поле dialog_id уйдёт на сервер
        /// со значением null, и сервер создаст новый диалог.
        /// </summary>
        public long? CurrentDialogId { get; private set; } = null;

        /// <summary>Защита от рекурсии при программной смене SelectedItem в списке диалогов.</summary>
        private bool _suppressDialogSelection = false;

        /// <summary>Ширина панели диалогов в пикселях. Пользователь меняет её сплиттером.</summary>
        private double _dialogsPanelWidth = 300;

        /// <summary>Гость = нет JWT-токена аккаунта.</summary>
        private bool IsGuest => !((App)Application.Current).IsLoggedIn;

        private string AccountToken => ((App)Application.Current).AccountToken;

        public AttachedFile GetAttachedFile() => _attachedFile;
        private dynamic _userData;

        private List<string> _actionTypes;
        public List<string> ActionTypes
        {
            get { return _actionTypes; }
            set { _actionTypes = value; OnPropertyChanged(nameof(ActionTypes)); }
        }

        // --- НОВЫЙ МЕТОД ДЛЯ СБОРА ЛОКАЛЬНОЙ ИСТОРИИ ---
        public List<object> GetGuestMessageHistory()
        {
            var history = new List<object>();
            // Берем последние 10 сообщений (игнорируя технические заглушки)
            var recentMessages = ChatMessages
                .Where(m => !string.IsNullOrEmpty(m.Text) && !m.Text.Contains("⏳") && !m.Text.Contains("🎤"))
                .Reverse().Take(10).Reverse();
            foreach (var msg in recentMessages)
            {
                history.Add(new { role = msg.IsUser ? "user" : "assistant", content = msg.Text });
            }
            return history;
        }

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
                // Если парсится в long, значит сообщение из БД. Иначе - это локальное сообщение гостя
                if (long.TryParse(_editingMessage.Id, out long serverMsgId))
                {
                    try
                    {
                        // Авторизованный правит своё сообщение по токену, гость — по MAC устройства.
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
                string screenshotBase64 = _attachedFile != null ? Convert.ToBase64String(_attachedFile.Data) : null;
                string pendingId = Guid.NewGuid().ToString();

                // Проверяем, гость мы или нет
                bool isGuest = IsGuest;

                // ВАЖНО: поле dialog_id отправляется ВСЕГДА, даже когда оно null.
                // Сервер различает "ключа нет" (взять последний диалог юзера)
                // и "ключ есть, но null" (создать НОВЫЙ диалог) — см. handle_command.
                var message = new
                {
                    type = "текстовое сообщение",
                    command = messageText,
                    mac = GetMacAddress(),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    name = SettingManager.Setting.AssistantName,
                    voice_type = SettingManager.Setting.VoiceType,
                    screenshot = screenshotBase64,
                    ui_msg_id = pendingId,
                    dialog_id = isGuest ? (long?)null : CurrentDialogId,
                    message_history = isGuest ? GetGuestMessageHistory() : null // Отправляем историю, если гость
                };

                ((App)Application.Current).VoiceService?.SetWaitingForServer();
                ((App)Application.Current).SendWebSocketMessage(message);

                ChatMessages.Add(new ChatMessage { Id = pendingId, UiMsgId = pendingId, Sender = "Вы", Text = messageText });
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
            DialogListBox.ItemsSource = Dialogs;
            _voiceService.OnChatMessageReceived += OnChatMessageReceived;
            InputModeComboBox.SelectionChanged += InputModeComboBox_SelectionChanged;

            DataContext = this;

            // Стартуем как гость; если аккаунт есть — придёт account_sync_success
            // и SyncAccountData() переключит режим.
            if (IsGuest) ApplyGuestMode();
            else ApplyAccountMode();
        }

        public void UpdateData(dynamic responseData)
        {
            if (responseData != null) ShowSystemMessage("Соединение восстановлено");
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
                if (!string.IsNullOrEmpty(e.AssistantName)) FridayNameTextBox.Text = e.AssistantName;
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
                Border thumbnailContainer = new Border { Background = Brushes.Transparent, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), CornerRadius = new CornerRadius(5), Width = 160, Height = 160 };
                Grid grid = new Grid();
                Image thumbnail = new Image { Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 150, MaxHeight = 150 };
                Button closeButton = new Button { Content = "×", Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0), Padding = new Thickness(0), FontWeight = FontWeights.Bold, Background = Brushes.White, Foreground = Brushes.Black, BorderThickness = new Thickness(0) };
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

        // ====================== РЕЖИМЫ ИНТЕРФЕЙСА ======================

        /// <summary>
        /// Режим АККАУНТА: показываем панель диалогов и ПРЯЧЕМ кнопку "Очистить".
        /// Точный аналог updateAuthUI() из script.js, где для залогиненного
        /// clear-history получает display:none — история чистится удалением диалога.
        /// </summary>
        public void ApplyAccountMode()
        {
            Dispatcher.Invoke(() =>
            {
                DialogsPanel.Visibility = Visibility.Visible;
                DialogsSplitter.Visibility = Visibility.Visible;
                // Ширину колонки задаём в пикселях — иначе GridSplitter не сможет её тянуть.
                DialogsColumn.Width = new GridLength(_dialogsPanelWidth);
                ClearHistoryButton.Visibility = Visibility.Collapsed;
            });
        }

        /// <summary>
        /// Режим ГОСТЯ: панели диалогов нет, кнопка "Очистить" доступна
        /// (чистит только локальный список — базу гость не трогает).
        /// </summary>
        public void ApplyGuestMode()
        {
            Dispatcher.Invoke(() =>
            {
                // Запоминаем ширину, которую пользователь выставил мышью,
                // чтобы вернуть её при следующем входе в аккаунт.
                if (DialogsPanel.Visibility == Visibility.Visible && DialogsColumn.ActualWidth > 1)
                    _dialogsPanelWidth = DialogsColumn.ActualWidth;

                DialogsPanel.Visibility = Visibility.Collapsed;
                DialogsSplitter.Visibility = Visibility.Collapsed;
                DialogsColumn.Width = new GridLength(0);   // колонка схлопывается -> чат на всю ширину
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
                    ShowAuthButtons();          // вернёт кнопку "Очистить" и спрячет панель диалогов
                    ChatMessages.Clear();
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
                if (!string.IsNullOrEmpty(login)) ShowUserButton(login); // включает ApplyAccountMode()
            });

            _ = SyncDialogsAfterLoginAsync(fallbackHistory);
        }

        /// <summary>
        /// После входа в аккаунт: тянем список диалогов и открываем самый свежий.
        /// Аналог связки loadDialogs() + selectDialog(dialogs[0].id) из script.js.
        /// </summary>
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
                    // Диалогов ещё нет — показываем историю из account_sync_success.
                    Dispatcher.Invoke(() => ProcessHistoryMessages(fallbackHistory));
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Dialogs] sync: {ex.Message}"); }
        }

        // ====================== РАБОТА СО СПИСКОМ ДИАЛОГОВ ======================

        /// <summary>Обновляет счётчик рядом с заголовком «ДИАЛОГИ».</summary>
        private void UpdateDialogsCount()
        {
            if (DialogsCountText != null)
                DialogsCountText.Text = Dialogs.Count > 0 ? Dialogs.Count.ToString() : "";
        }

        /// <summary>Подтягивает список диалогов с сервера. Аналог loadDialogs().</summary>
        public async Task LoadDialogsAsync()
        {
            if (IsGuest) { ApplyGuestMode(); return; }

            var list = await DialogService.GetDialogsAsync(AccountToken);

            Dispatcher.Invoke(() =>
            {
                _suppressDialogSelection = true;
                Dialogs.Clear();
                foreach (var d in list) Dialogs.Add(d);

                // Восстанавливаем подсветку активного диалога
                var active = Dialogs.FirstOrDefault(d => CurrentDialogId.HasValue && d.Id == CurrentDialogId.Value);
                DialogListBox.SelectedItem = active;
                _suppressDialogSelection = false;
                UpdateDialogsCount();
            });
        }

        /// <summary>Открывает диалог: чистит чат и грузит его историю. Аналог selectDialog().</summary>
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

        /// <summary>
        /// Кнопка "Новый чат". Диалог НЕ создаётся здесь — сервер заведёт его сам,
        /// когда получит первое сообщение с dialog_id = null, и пришлёт dialog_created.
        /// </summary>
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

        /// <summary>Крестик на элементе списка — удаление диалога вместе с историей.</summary>
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

        /// <summary>Сервер создал диалог автоматически (пришёл кадр dialog_created).</summary>
        public async void OnDialogCreated(long dialogId, string name)
        {
            CurrentDialogId = dialogId;
            await LoadDialogsAsync();
        }

        /// <summary>ИИ дал диалогу осмысленное имя (кадр dialog_renamed) — правим на лету.</summary>
        public void OnDialogRenamed(long dialogId, string name)
        {
            Dispatcher.Invoke(() =>
            {
                var d = Dialogs.FirstOrDefault(x => x.Id == dialogId);
                if (d != null) d.Name = name;   // INotifyPropertyChanged обновит UI
            });
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e) => new LoginWindow().ShowDialog();
        private void RegisterButton_Click(object sender, RoutedEventArgs e) => new RegisterWindow().ShowDialog();

        public void UpdateAfterRegistration(string username)
        {
            Dispatcher.Invoke(() => { ShowUserButton(username); ShowSystemMessage($"Добро пожаловать, {username}!"); });
        }

        private void OnMessageReceived(string message) { Dispatcher.Invoke(() => { ChatListBox.Items.Add(message + Environment.NewLine); ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]); }); }
        private void OnChatMessageReceived(ChatMessage msg) { Dispatcher.Invoke(() => { ChatMessages.Add(msg); ChatListBox.UpdateLayout(); ChatListBox.ScrollIntoView(msg); }); }
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
            FridayNameTextBox.Text = SettingManager.Setting.AssistantName;
            foreach (ComboBoxItem item in VoiceTypeComboBox.Items) { if (item.Content.ToString() == SettingManager.Setting.VoiceType) { VoiceTypeComboBox.SelectedItem = item; break; } }
            VolumeSlider.Value = SettingManager.Setting.Volume;
            foreach (ComboBoxItem item in InputModeComboBox.Items) { if (item.Content.ToString() == SettingManager.Setting.InputMode) { InputModeComboBox.SelectedItem = item; break; } }
            MusicFolderPathTextBox.Text = SettingManager.Setting.MusicFolderPath;
        }

        private void ChooseMusicFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Выберите папку с музыкой", InitialDirectory = Directory.Exists(MusicFolderPathTextBox.Text) ? MusicFolderPathTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) };
            if (dialog.ShowDialog() == true) MusicFolderPathTextBox.Text = dialog.FolderName;
        }

        public void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            string assistantName = FridayNameTextBox.Text;
            string voiceType = (VoiceTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            int volume = Convert.ToInt32(VolumeSlider.Value);
            string inputMode = (InputModeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            string musicFolder = MusicFolderPathTextBox.Text;

            if (string.IsNullOrEmpty(assistantName) || string.IsNullOrEmpty(voiceType) || string.IsNullOrEmpty(inputMode)) { MessageBox.Show("Поля не могут быть пустыми", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _settingManager.UpdateSettings(assistantName, SettingManager.Setting.Password, voiceType, volume, inputMode, musicFolder);
            MessageBox.Show("Настройки успешно обновлены!", "Успех!", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ClearHistoryButton_Click(object sender, RoutedEventArgs e) => ClearHistory();

        /// <summary>
        /// Очистка истории — ТОЛЬКО для гостевого режима (чистит локальный список).
        ///
        /// У авторизованного история живёт в диалоге на сервере и чистится
        /// удалением диалога через крестик в левой панели. Кнопка "Очистить"
        /// для него скрыта (ApplyAccountMode), а серверный action "очистка истории"
        /// вырезается из списка возможностей ИИ при наличии dialog_id.
        /// Обращаться к /clear_history нельзя — такого эндпоинта на сервере НЕТ.
        /// </summary>
        public void ClearHistory()
        {
            if (!IsGuest)
            {
                ShowSystemMessage("Чтобы очистить историю, удалите диалог в списке слева.");
                return;
            }

            Dispatcher.Invoke(() => ChatMessages.Clear());
            ShowSystemMessage("История успешно очищена");
        }

        public void ManageAppsButton_Click(object sender, RoutedEventArgs e) => new Friday.Windows.AppPathsWindow().ShowDialog();
        public void ClearAttachedFile() { _attachedFile = null; ThumbnailContainer.Items.Clear(); }
        public void ChangedataButton_Click(object sender, RoutedEventArgs e) => new ChangeDataWindow().Show();
        protected override void OnClosed(EventArgs e) { _settingManager.SettingsChanged -= SettingManager_SettingsChanged; ((App)Application.Current).DecrementWindowCount(); base.OnClosed(e); }
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
                        // Авторизованный удаляет по токену (проверка владельца диалога),
                        // гость — по MAC своего устройства.
                        object requestData = IsGuest
                            ? (object)new { msg_id = serverMsgId, mac = GetMacAddress() }
                            : (object)new { msg_id = serverMsgId, token = AccountToken };
                        using (var client = new HttpClient())
                        {
                            var json = JsonConvert.SerializeObject(requestData);
                            var content = new StringContent(json, Encoding.UTF8, "application/json");
                            var response = await client.PostAsync("https://friday-assistant.ru/delete_message", content);
                            if (response.IsSuccessStatusCode) ChatMessages.Remove(msg);
                            else ShowSystemMessage("Ошибка при удалении сообщения с сервера.");
                        }
                    }
                    catch (Exception ex) { ShowSystemMessage($"Ошибка сети при удалении: {ex.Message}"); }
                }
                else ChatMessages.Remove(msg); // Удаляем локально для гостей
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
                    Image thumbnail = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = 150, MaxHeight = 150 };
                    Button closeButton = new Button { Content = "×", Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                    closeButton.Click += (s, ev) => ClearAttachedFile();
                    grid.Children.Add(thumbnail); grid.Children.Add(closeButton); thumbnailContainer.Child = grid;
                    ThumbnailContainer.Items.Add(thumbnailContainer);
                }
            }
            catch (Exception ex) { ShowSystemMessage($"Не удалось отобразить скриншот: {ex.Message}"); }
        }
    }

    public enum InputMode { NamePlusCommand, Conversation }

    public class MessageAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return value is string text && text.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase) ? HorizontalAlignment.Right : HorizontalAlignment.Left; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class MessageBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return value is string text && text.StartsWith("Вы:", StringComparison.OrdinalIgnoreCase) ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6A3D8B")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#625B71")); }
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