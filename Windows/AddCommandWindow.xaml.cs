using System.Windows;
using Friday.Managers;
using System.Windows.Controls;
using System.Windows.Input;

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
            Actions = new List<ActionItem>();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
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
                    string currentActionText = actionTextBlock.Text;

                    var parts = currentActionText.Split(new[] { ": " }, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        string currentActionType = parts[0];
                        string currentAction = parts[1];

                        var inputDialog = new InputDialog("Редактировать действие", currentAction, currentActionType);
                        if (inputDialog.ShowDialog() == true)
                        {
                            // Обновляем текст в интерфейсе
                            actionTextBlock.Text = $"{inputDialog.ActionType}: {inputDialog.InputText}";

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
                        ActionsItemsControl.Items.Remove(actionTextBlock.Text); // Удаляем из интерфейса
                    }
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

            // Убираем проверку на наличие действий, если это редактирование
            if (Actions.Count == 0 && !isEditing) // Проверяем, есть ли действия только если не редактируем
            {
                MessageBox.Show("Пожалуйста, добавьте хотя бы одно действие.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Логика для добавления команды
            this.DialogResult = true; // Устанавливаем результат диалога как успешный
            this.Close(); // Закрытие окна после добавления команды
        }
    }
}

