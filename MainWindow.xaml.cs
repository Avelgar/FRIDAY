using System.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using Friday;
using System.Windows.Media;
using System.Windows.Input;
using Friday.Managers;
using System.Management;

namespace FigmaToWpf
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private VoiceService _voiceService;
        public static Friday.CommandManager _commandManager = new Friday.CommandManager();
        private static SettingManager _settingManager = new SettingManager();

        public List<string> InstalledApplications { get; private set; }

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

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();

            RenameService renameService = new RenameService(_settingManager.Setting.AssistantName);
            _voiceService = new VoiceService(renameService, _settingManager);

            _voiceService.OnMessageReceived += OnMessageReceived;
            CustomCommandService.Initialize(_voiceService);

            InstalledApplications = GetInstalledApplications();
            _voiceService.SetInstalledApplications(InstalledApplications);

            // Инициализируем Commands и подписываемся на изменение текста в SearchTextBox
            Commands = new ObservableCollection<Command>(_commandManager.GetCommands());
            CommandsItemsControl.ItemsSource = Commands;  // Привязка к Commands, а не напрямую к _commandManager
            SearchTextBox.TextChanged += SearchTextBox_TextChanged; // Подписываемся на событие изменения текста

            // Заполняем ActionTypes и ComboBox
            LoadActionTypes();
            ActionTypeComboBox.ItemsSource = ActionTypes;

            DataContext = this; // Необходимо для работы привязки Commands
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

        private List<string> GetInstalledApplications()
        {
            List<string> appPaths = new List<string>();

            // Путь к ключам реестра, где хранятся установленные приложения
            string[] registryKeys = new string[]
            {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var key in registryKeys)
            {
                using (var uninstallKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key))
                {
                    if (uninstallKey != null)
                    {
                        foreach (var subkeyName in uninstallKey.GetSubKeyNames())
                        {
                            using (var subkey = uninstallKey.OpenSubKey(subkeyName))
                            {
                                // Получаем путь установки
                                var installLocation = subkey?.GetValue("InstallLocation") as string;
                                if (!string.IsNullOrEmpty(installLocation))
                                {
                                    appPaths.Add(installLocation);
                                }
                            }
                        }
                    }
                }
            }

            return appPaths;
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
    }
}
