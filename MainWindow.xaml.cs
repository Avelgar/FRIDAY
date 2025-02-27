using System;
using System.Windows;
using System.Windows.Controls;
using Friday;
using Friday.Managers;
using System.Windows.Media;

namespace FigmaToWpf
{
    public partial class MainWindow : Window
    {
        private VoiceService _voiceService;

        public MainWindow()
        {
            InitializeComponent();
            // Создаем экземпляр RenameService
            RenameService renameService = new RenameService();
            // Передаем его в конструктор VoiceService
            _voiceService = new VoiceService(renameService);
            _voiceService.OnMessageReceived += OnMessageReceived;
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
                ListenButton.Content = "Начать слушать";
                ConsoleTextBox.AppendText("Слушание остановлено." + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            }
            else
            {
                _voiceService.ListeningState.StartListening();
                ListenButton.Content = "Остановить слушать";
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
            AddCommandWindow addCommandWindow = new AddCommandWindow();
            addCommandWindow.CommandAdded += AddCommandToList; // Подписываемся на событие
            addCommandWindow.ShowDialog();
        }

        private void AddCommandToList(Command command)
        {
            // Добавляем команду в ItemsControl
            var commandPanel = new StackPanel { Margin = new Thickness(10) };
            commandPanel.Children.Add(new TextBlock { Text = $"ID: {command.Id}", Foreground = Brushes.LightGray });
            commandPanel.Children.Add(new TextBlock { Text = command.Name, Foreground = Brushes.White, FontWeight = FontWeights.Bold });
            commandPanel.Children.Add(new TextBlock { Text = command.Description, Foreground = Brushes.LightGray });

            // Добавляем действия
            foreach (var action in command.Actions)
            {
                commandPanel.Children.Add(new TextBlock { Text = action, Foreground = Brushes.LightGray });
            }

            // Кнопка редактирования
            var editButton = new Button { Content = "Редактировать", Margin = new Thickness(5), Width = 100 };
            editButton.Click += (s, e) => EditCommand(command, commandPanel);
            commandPanel.Children.Add(editButton);

            // Кнопка удаления
            var deleteButton = new Button { Content = "Удалить", Margin = new Thickness(5), Width = 100 };
            deleteButton.Click += (s, e) => CommandsItemsControl.Items.Remove(commandPanel);
            commandPanel.Children.Add(deleteButton);

            // Добавляем команду в ItemsControl
            CommandsItemsControl.Items.Add(commandPanel);
        }

        private void EditCommand(Command command, StackPanel commandPanel)
        {
            // Открываем окно редактирования с заполненными данными
            var addCommandWindow = new AddCommandWindow(command); // Передаем текущую команду

            addCommandWindow.CommandAdded += (editedCommand) =>
            {
                // Обновляем команду в списке
                command.Name = editedCommand.Name;
                command.Description = editedCommand.Description;
                command.Actions = editedCommand.Actions;

                // Обновляем текст в commandPanel
                ((TextBlock)commandPanel.Children[1]).Text = command.Name;
                ((TextBlock)commandPanel.Children[2]).Text = command.Description;
                commandPanel.Children.Clear(); // Очищаем старые элементы
                commandPanel.Children.Add(new TextBlock { Text = $"ID: {command.Id}", Foreground = Brushes.LightGray });
                commandPanel.Children.Add(new TextBlock { Text = command.Name, Foreground = Brushes.White, FontWeight = FontWeights.Bold });
                commandPanel.Children.Add(new TextBlock { Text = command.Description, Foreground = Brushes.LightGray });

                // Добавляем обновленные действия
                foreach (var action in command.Actions)
                {
                    commandPanel.Children.Add(new TextBlock { Text = action, Foreground = Brushes.LightGray });
                }

                // Создаем кнопки редактирования и удаления
                var editButton = new Button { Content = "Редактировать", Margin = new Thickness(5), Width = 100 };
                editButton.Click += (s, e) => EditCommand(command, commandPanel);
                commandPanel.Children.Add(editButton);

                var deleteButton = new Button { Content = "Удалить", Margin = new Thickness(5), Width = 100 };
                deleteButton.Click += (s, e) => CommandsItemsControl.Items.Remove(commandPanel);
                commandPanel.Children.Add(deleteButton);
            };

            addCommandWindow.ShowDialog();
        }


    }
}
