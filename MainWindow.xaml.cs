using System;
using System.Windows;
using System.Windows.Controls;
using Friday;
using Friday.Managers;
using System.Windows.Media;
using System.Windows.Input;

namespace FigmaToWpf
{
    public partial class MainWindow : Window
    {
        private VoiceService _voiceService;
        public static Friday.CommandManager _commandManager = new Friday.CommandManager();
        private static SettingManager _settingManager = new SettingManager();
        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            RenameService renameService = new RenameService();
            _voiceService = new VoiceService(renameService);
            _voiceService.OnMessageReceived += OnMessageReceived;
            CustomCommandService.Initialize(_voiceService);
            // Инициализируем список команд при старте приложения
            UpdateCommandsList();

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
                //ListenButton.Content = "Начать слушать";
                _voiceService.StopListening();
                ConsoleTextBox.AppendText("Слушание остановлено." + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            }
            else
            {
                _voiceService.ListeningState.StartListening();
                //ListenButton.Content = "Остановить слушать";
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
                // Получаем значения из открытого окна
                string name = addCommandWindow.CommandName;
                string description = addCommandWindow.Description;
                var actions = addCommandWindow.Actions; // Убедитесь, что это List<ActionItem>
                bool isPasswordSet = addCommandWindow.IsPasswordSet;

                // Добавляем команду в CommandManager
                _commandManager.AddCommand(name, description, actions, isPasswordSet);

                UpdateCommandsList(); // Обновляем список команд на интерфейсе
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
            CommandsItemsControl.ItemsSource = null; // Сбрасываем источник данных
            CommandsItemsControl.ItemsSource = _commandManager.GetCommands(); // Устанавливаем новый источник данных
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
                                // Получаем обновленные значения из окна
                                string newName = addCommandWindow.CommandName;
                                string newDescription = addCommandWindow.Description;
                                var newActions = addCommandWindow.Actions; // Убедитесь, что это List<ActionItem>
                                bool isPasswordSet = addCommandWindow.IsPasswordSet;

                                // Обновляем команду в CommandManager
                                _commandManager.EditCommand(commandToEdit.Id, newName, newDescription, newActions, isPasswordSet);

                                UpdateCommandsList(); // Обновляем список команд на интерфейсе
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
                // Находим родительский Border, который содержит StackPanel
                var border = FindParent<Border>(button);
                if (border != null)
                {
                    // Находим TextBlock с именем команды внутри Border
                    var nameTextBlock = FindChild<TextBlock>(border, "NameTextBlock");
                    if (nameTextBlock != null)
                    {
                        string commandName = nameTextBlock.Text.Trim();
                        MessageBoxResult result = MessageBox.Show($"Вы уверены, что хотите удалить команду: {commandName}?", "Подтверждение удаления", MessageBoxButton.YesNo);

                        if (result == MessageBoxResult.Yes)
                        {
                            // Удаляем команду через CommandManager
                            _commandManager.DeleteCommand(commandName); // Используем уже созданный экземпляр

                            // Обновляем интерфейс, чтобы отобразить изменения
                            UpdateCommandsList(); // Метод для обновления интерфейса
                        }
                    }
                }
            }
        }


        // Метод для поиска родительского элемента определенного типа
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;

            T parent = parentObject as T;
            return parent != null ? parent : FindParent<T>(parentObject);
        }

        // Метод для поиска дочернего элемента определенного типа
        private T FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            // Проверяем, есть ли у родителя дочерние элементы
            if (parent == null) return null;

            T foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                // Проверяем, является ли дочерний элемент нужного типа
                T childType = child as T;
                if (childType != null && !string.IsNullOrEmpty(childName))
                {
                    // Если у дочернего элемента есть имя, сравниваем его с искомым
                    var frameworkElement = child as FrameworkElement;
                    if (frameworkElement != null && frameworkElement.Name == childName)
                    {
                        foundChild = (T)child;
                        break;
                    }
                }
                else
                {
                    // Рекурсивно ищем в дочерних элементах
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
            if (string.IsNullOrEmpty(password)){ password = _settingManager.Setting.Password; }
            _settingManager.UpdateSettings(assistantName, password, voiceType, volume);
            MessageBox.Show("Настройки успешно обновлены!", "Успех!", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
