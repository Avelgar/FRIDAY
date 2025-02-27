using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Friday.Managers;
using System.Windows.Media;

namespace FigmaToWpf
{
    public partial class AddCommandWindow : Window
    {
        public event Action<Command> CommandAdded; // Событие для передачи команды
        private static int _nextId = 1;
        private Command _currentCommand;

        public AddCommandWindow(Command command = null)
        {
            InitializeComponent();
            _currentCommand = command;

            if (_currentCommand != null)
            {
                // Заполняем поля значениями текущей команды
                CommandNameTextBox.Text = _currentCommand.Name;
                DescriptionTextBox.Text = _currentCommand.Description;

                // Заполняем ActionsItemsControl
                foreach (var action in _currentCommand.Actions)
                {
                    AddAction(action);
                }
            }
        }


        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // Проверка на заполненность полей
            if (string.IsNullOrWhiteSpace(CommandNameTextBox.Text))
            {
                MessageBox.Show("Пожалуйста, введите имя команды.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(DescriptionTextBox.Text))
            {
                MessageBox.Show("Пожалуйста, введите описание.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ActionsItemsControl.Items.Count == 0)
            {
                MessageBox.Show("Пожалуйста, добавьте хотя бы одно действие.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var command = new Command
            {
                Id = _currentCommand?.Id ?? _nextId++, // Используем существующий ID или создаем новый
                Name = CommandNameTextBox.Text,
                Description = DescriptionTextBox.Text,
                Actions = ActionsItemsControl.Items.Cast<StackPanel>().Select(sp => ((TextBlock)sp.Children[0]).Text).ToList()
            };

            CommandAdded?.Invoke(command); // Вызываем событие

            CommandNameTextBox.Clear();
            DescriptionTextBox.Clear();
            ActionsItemsControl.Items.Clear();
            Close();
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Закрываем окно
        }

        private void VoiceResponseButton_Click(object sender, RoutedEventArgs e)
        {
            var inputDialog = new InputDialog();
            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.InputText))
            {
                AddAction($"Голосовой ответ: {inputDialog.InputText}");
            }
        }

        private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
        {
            var inputDialog = new InputDialog();
            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.InputText))
            {
                AddAction($"Открытие ссылки: {inputDialog.InputText}");
            }
        }

        private void AddAction(string actionText)
        {
            // Создаем плашку для действия
            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
            var actionLabel = new TextBlock { Text = actionText, Foreground = Brushes.White, Margin = new Thickness(0, 0, 5, 0) };
            var editButton = new Button { Content = "✏️", Width = 20, Height = 20, Background = Brushes.Transparent, Foreground = Brushes.White };
            var removeButton = new Button { Content = "✖️", Width = 20, Height = 20, Background = Brushes.Transparent, Foreground = Brushes.White };

            // Обработчик для редактирования действия
            editButton.Click += (s, e) =>
            {
                // Получаем текст после двоеточия
                string[] parts = actionText.Split(new[] { ": " }, 2, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    var inputDialog = new InputDialog();
                    inputDialog.InputText = parts[1]; // Передаем текст для редактирования
                    if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.InputText))
                    {
                        actionLabel.Text = $"{parts[0]}: {inputDialog.InputText}"; // Обновляем текст действия
                    }
                }
            };

            removeButton.Click += (s, e) => ActionsItemsControl.Items.Remove(actionPanel); // Удаляем плашку при нажатии

            actionPanel.Children.Add(actionLabel);
            actionPanel.Children.Add(editButton);
            actionPanel.Children.Add(removeButton);
            ActionsItemsControl.Items.Add(actionPanel); // Добавляем плашку в ItemsControl
        }
    }
}
