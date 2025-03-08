using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace FigmaToWpf
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; }
        public string ActionType { get; private set; }


        // Конструктор с параметрами
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

        private bool isFirstLoad = true;

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void ActionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
            {
                string selectedProcessName = ProcessComboBox.SelectedItem.ToString();
                InputTextBox.Text = selectedProcessName;
            }
        }

        private void UpdateInputField(string actionType)
        {
            InputTextBox.MaxLength = actionType switch
            {
                "Голосовой ответ" => 100,
                "Завершение процесса" => 0,
                _ => 100
            };

            if (!isFirstLoad)
            {
                InputTextBox.Clear();
            }

            isFirstLoad = false;

            FileButton.Visibility = actionType == "Открытие файла" ? Visibility.Visible : Visibility.Collapsed;
            ProcessComboBox.Visibility = actionType == "Завершение процесса" ? Visibility.Visible : Visibility.Collapsed;
            InputTextBox.PreviewTextInput -= TextBox_PreviewTextInput_Numbers;
            InputTextBox.PreviewTextInput -= TextBox_PreviewTextInput_Russian;

            if (actionType == "Завершение процесса")
            {
                LoadProcesses();
            }

            if (actionType == "Голосовой ответ")
            {
                InputTextBox.PreviewTextInput += TextBox_PreviewTextInput_Russian;
            }
            else
            {
                InputTextBox.PreviewTextInput += TextBox_PreviewTextInput_Numbers;
            }
        }

        private void LoadProcesses()
        {
            //ProcessComboBox.Items.Clear();
            //var processes = System.Diagnostics.Process.GetProcesses();
            //foreach (var process in processes)
            //{
            //    ProcessComboBox.Items.Add(process.ProcessName);
            //}
            ProcessComboBox.Items.Clear(); // Очищаем предыдущие элементы
            var processes = System.Diagnostics.Process.GetProcesses(); // Получаем все запущенные процессы

            foreach (var process in processes)
            {
                try
                {
                    // Проверяем, что у процесса есть окно и оно отображается
                    if (!string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        ProcessComboBox.Items.Add(process.ProcessName); // Добавляем имя процесса в ComboBox
                    }
                }
                catch (Exception ex)
                {

                }
            }
        }

        private void TextBox_PreviewTextInput_Numbers(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$"); // Разрешаем только цифры
        }

        private void TextBox_PreviewTextInput_Russian(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[а-яё\s]+$"); // Разрешаем только русские строчные символы
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, что выбран тип действия
            if (ActionTypeComboBox.SelectedItem == null)
            {
                MessageBox.Show("Пожалуйста, выберите тип действия.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedAction = (ActionTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();

            // Проверяем, что текстовое поле не пустое
            if (string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                MessageBox.Show("Пожалуйста, введите необходимые данные.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Дополнительные проверки на корректность введённых данных
            // if (selectedAction == "Изменение громкости" &&
            //     (!Regex.IsMatch(InputTextBox.Text, @"^\d{1,3}$") ||
            //     int.Parse(InputTextBox.Text) < 0 || int.Parse(InputTextBox.Text) > 100))
            // {
            //     MessageBox.Show("Введите корректное значение громкости от 0 до 100.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            //     return;
            // }

            // Сохраняем введенные данные и тип действия
            InputText = InputTextBox.Text;
            ActionType = selectedAction; // Сохраняем выбранный тип действия
            DialogResult = true; // Устанавливаем результат диалога как успешный
            Close(); // Закрываем диалог
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Закрытие окна
        }

        private void FileButton_Click(object sender, RoutedEventArgs e)
        {
            // Открываем диалог выбора файла
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*", // Фильтр файлов
                Title = "Выберите файл"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InputTextBox.Text = openFileDialog.FileName; // Устанавливаем путь к выбранному файлу в текстовое поле
            }
        }
    }
}
