using System.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using Friday;
using System.Windows.Media;
using System.Windows.Input;
using Friday.Managers;

namespace FigmaToWpf
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private VoiceService _voiceService;
        public static Friday.CommandManager _commandManager = new Friday.CommandManager();
        private static SettingManager _settingManager = new SettingManager();

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

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();

            RenameService renameService = new RenameService(_settingManager.Setting.AssistantName);
            _voiceService = new VoiceService(renameService);

            _voiceService.OnMessageReceived += OnMessageReceived;
            CustomCommandService.Initialize(_voiceService);

            // Инициализируем Commands и подписываемся на изменение текста в SearchTextBox
            Commands = new ObservableCollection<Command>(_commandManager.GetCommands());
            CommandsItemsControl.ItemsSource = Commands;  // Привязка к Commands, а не напрямую к _commandManager
            SearchTextBox.TextChanged += SearchTextBox_TextChanged; // Подписываемся на событие изменения текста

            DataContext = this; // Необходимо для работы привязки Commands
        }

        // Реализация INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterCommands();
        }

        private void FilterCommands()
        {
            string searchText = SearchTextBox.Text;

            if (string.IsNullOrEmpty(searchText) || searchText == "Search")
            {
                // Если строка поиска пуста, отображаем все команды
                Commands = new ObservableCollection<Command>(_commandManager.GetCommands());
            }
            else
            {
                // Фильтруем команды на основе текста поиска
                Commands = new ObservableCollection<Command>(_commandManager.GetCommands()
                    .Where(c => c.Name.ToLower().Contains(searchText.ToLower()) ||
                                c.Description.ToLower().Contains(searchText.ToLower()))
                    .ToList());
            }
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
                ConsoleTextBox.AppendText("Слушание остановлено." + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            }
            else
            {
                _voiceService.ListeningState.StartListening();
                _voiceService.StartListening();
                ConsoleTextBox.AppendText("Начинаю слушать..." + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            }
        }

        private void OnMessageReceived(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ConsoleTextBox.AppendText(message + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            });
        }

        private void AddCommandButton_Click(object sender, RoutedEventArgs e)
        {
            var addCommandWindow = new AddCommandWindow();

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
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void UpdateCommandsList()
        {
            // После обновления команд в _commandManager, обновляем и Commands, чтобы отобразить изменения
            Commands = new ObservableCollection<Command>(_commandManager.GetCommands());
            CommandsItemsControl.ItemsSource = Commands; // Обновляем ItemsSource
        }
        private void EditCommandButton_Click(object sender, RoutedEventArgs e)
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
                            var addCommandWindow = new AddCommandWindow();
                            addCommandWindow.Initialize(commandToEdit.Name, commandToEdit.Description, commandToEdit.Actions, commandToEdit.IsPassword);

                            if (addCommandWindow.ShowDialog() == true)
                            {
                                string newName = addCommandWindow.CommandName;
                                string newDescription = addCommandWindow.Description;
                                var newActions = addCommandWindow.Actions;
                                bool isPasswordSet = addCommandWindow.IsPasswordSet;

                                _commandManager.EditCommand(commandToEdit.Id, newName, newDescription, newActions, isPasswordSet);

                                UpdateCommandsList();
                            }
                        }
                    }
                }
            }
        }



        private void DeleteCommandButton_Click(object sender, RoutedEventArgs e)
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
        private void Save_Button_Click(object sender, RoutedEventArgs e)
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
    }
}
