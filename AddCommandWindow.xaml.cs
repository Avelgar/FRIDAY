using System.Windows;
using Friday.Managers; // Убедитесь, что это пространство имен соответствует вашему проекту
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq; // Добавлено для использования LINQ
using System;
using static System.Windows.Forms.Design.AxImporter;

namespace FigmaToWpf
{
    public partial class AddCommandWindow : Window
    {
        public string CommandName { get; set; }
        public string Description { get; set; }
        public List<ActionItem> Actions { get; set; }
        public bool IsPasswordSet { get; set; }

        private bool isEditing;

        public AddCommandWindow()
        {
            InitializeComponent();
            Actions = new List<ActionItem>(); // Инициализация списка действий
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Закрытие окна
        }

        public void Initialize(string commandName, string description, List<ActionItem> actions, bool isPassword)
        {
            CommandNameTextBox.Text = commandName;
            DescriptionTextBox.Text = description;
            Actions = actions ?? new List<ActionItem>(); // Инициализация, если actions равно null
            IsPasswordSet = isPassword;
            SetPasswordCheckBox.IsChecked = isPassword;

            // Обновляем ItemsControl с действиями
            ActionsItemsControl.Items.Clear();
            foreach (var action in Actions)
            {
                ActionsItemsControl.Items.Add($"{action.ActionType}: {action.ActionText}");
            }

            isEditing = true; // Устанавливаем флаг редактирования
        }

        private void AddActionButton_Click(object sender, RoutedEventArgs e)
        {
            string title = "Добавить действие";
            var inputDialog = new InputDialog(title, "", "");

            if (inputDialog.ShowDialog() == true)
            {
                string newActionText = $"{inputDialog.ActionType}: {inputDialog.InputText}"; // Формат "тип: действие"
                ActionsItemsControl.Items.Add(newActionText);

                int actionId = Actions.Count + 1; // Генерация ID для действия
                var newAction = new ActionItem(actionId, inputDialog.ActionType, inputDialog.InputText);
                Actions.Add(newAction); // Добавляем действие в список
            }
        }


        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Parent is StackPanel stackPanel)
            {
                TextBlock actionTextBlock = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
                if (actionTextBlock != null)
                {
                    string title = "Редактировать действие";
                    string currentActionText = actionTextBlock.Text;

                    var parts = currentActionText.Split(new[] { ": " }, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        string currentActionType = parts[0];
                        string currentAction = parts[1];

                        var inputDialog = new InputDialog(title, currentAction, currentActionType);
                        if (inputDialog.ShowDialog() == true)
                        {
                            string newActionText = $"{inputDialog.ActionType}: {inputDialog.InputText}";
                            actionTextBlock.Text = newActionText; // Обновляем текст в интерфейсе

                            // Обновляем действие в списке Actions
                            var actionToEdit = Actions.FirstOrDefault(a => a.ActionType == currentActionType && a.ActionText == currentAction);
                            if (actionToEdit != null)
                            {
                                actionToEdit.ActionType = inputDialog.ActionType;
                                actionToEdit.ActionText = inputDialog.InputText;
                            }
                        }
                    }
                }
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Parent is StackPanel stackPanel)
            {
                TextBlock actionTextBlock = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
                if (actionTextBlock != null)
                {
                    var actionToRemove = Actions.FirstOrDefault(a => $"{a.ActionType}: {a.ActionText}" == actionTextBlock.Text);
                    if (actionToRemove != null)
                    {
                        Actions.Remove(actionToRemove); // Удаляем действие из списка
                    }
                    ActionsItemsControl.Items.Remove(actionTextBlock.Text); // Удаляем из интерфейса
                }
            }
        }


        private void CommandNameTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Проверяем, что вводимый символ является русским строчным символом или пробелом
            char inputChar = e.Text[0];
            if (!IsValidInput(inputChar))
            {
                e.Handled = true; // Отменяем ввод, если символ недопустим
            }
        }

        private bool IsValidInput(char c)
        {
            // Проверяем, является ли символ русской строчной буквой или пробелом
            return (c >= 'а' && c <= 'я') || c == 'ё' || c == ' ';
        }
        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            CommandName = CommandNameTextBox.Text.Trim();
            Description = DescriptionTextBox.Text.Trim();
            IsPasswordSet = SetPasswordCheckBox.IsChecked ?? false;

            // Проверка на заполненность полей
            if (string.IsNullOrEmpty(CommandName))
            {
                MessageBox.Show("Пожалуйста, введите имя команды.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(Description))
            {
                MessageBox.Show("Пожалуйста, введите описание команды.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ActionsItemsControl.Items.Count == 0)
            {
                MessageBox.Show("Пожалуйста, добавьте хотя бы одно действие.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Если редактируем, устанавливаем флаг для обновления
            if (isEditing)
            {
                // Здесь вы можете обновить данные команды
                MessageBox.Show("Команда обновлена успешно!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Здесь вы можете обработать добавление новой команды
                MessageBox.Show("Команда добавлена успешно!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            this.DialogResult = true; // Устанавливаем результат диалога как успешный
            this.Close(); // Закрытие окна после добавления или редактирования команды
        }

    }
}

