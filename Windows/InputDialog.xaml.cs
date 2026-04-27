using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Friday
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; }
        public string ActionType { get; private set; }

        private bool isFirstLoad = true;

        // Списки для настройки умного UI
        private readonly string[] _parameterlessActions = { "Очистка истории", "Режим камеры", "Выключить режим камеры", "Скриншот" };
        private readonly string[] _numericActions = { "Ожидание", "Изменение громкости", "Изменение яркости" };

        public InputDialog(string title, string commandText, string actionType)
        {
            InitializeComponent();
            Title = title;
            InputTextBox.Text = commandText;

            if (!string.IsNullOrEmpty(actionType))
            {
                ActionTypeComboBox.SelectedItem = ActionTypeComboBox.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(item => item.Content.ToString() == actionType);
            }

            ProcessComboBox.SelectionChanged += ProcessComboBox_SelectionChanged;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        public void ActionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ActionTypeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedAction = selectedItem.Content.ToString();
                UpdateInputField(selectedAction);
            }
        }

        private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProcessComboBox.SelectedItem != null)
                InputTextBox.Text = ProcessComboBox.SelectedItem.ToString();
        }

        private void UpdateInputField(string actionType)
        {
            if (!isFirstLoad) InputTextBox.Clear();
            isFirstLoad = false;

            // Настройка видимости кнопок
            FileButton.Visibility = actionType == "Открытие файла" ? Visibility.Visible : Visibility.Collapsed;
            SelectFolderButton.Visibility = actionType == "Открыть папку" ? Visibility.Visible : Visibility.Collapsed;
            ProcessComboBox.Visibility = actionType == "Завершение процесса" ? Visibility.Visible : Visibility.Collapsed;

            // Скрываем текстовое поле, если параметры не нужны
            bool needsInput = !_parameterlessActions.Contains(actionType);
            InputTextBox.Visibility = needsInput ? Visibility.Visible : Visibility.Collapsed;

            // Отписываемся от старых событий
            InputTextBox.PreviewTextInput -= TextBox_PreviewTextInput_Numbers;

            // Подписываемся на нужные фильтры
            if (actionType == "Завершение процесса")
            {
                LoadProcesses();
            }
            else if (_numericActions.Contains(actionType))
            {
                InputTextBox.PreviewTextInput += TextBox_PreviewTextInput_Numbers;
            }
        }

        private void LoadProcesses()
        {
            ProcessComboBox.Items.Clear();
            var processes = System.Diagnostics.Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    if (!string.IsNullOrEmpty(process.MainWindowTitle))
                        ProcessComboBox.Items.Add(process.ProcessName);
                }
                catch { }
            }
        }

        private void TextBox_PreviewTextInput_Numbers(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Разрешаем только цифры
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");
        }

        public void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (ActionTypeComboBox.SelectedItem == null)
            {
                MessageBox.Show("Пожалуйста, выберите тип действия.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedAction = (ActionTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            bool needsInput = !_parameterlessActions.Contains(selectedAction);

            if (needsInput && string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                MessageBox.Show("Пожалуйста, введите необходимые данные.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Записываем пустую строку, если параметры не нужны, чтобы UI в конструкторе выглядел красиво
            InputText = needsInput ? InputTextBox.Text : "";
            ActionType = selectedAction;

            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => this.Close();

        private void FileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "All Files (*.*)|*.*", Title = "Выберите файл" };
            if (openFileDialog.ShowDialog() == true) InputTextBox.Text = openFileDialog.FileName;
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                InputTextBox.Text = folderDialog.SelectedPath;
        }
    }
}