using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32; // Для открытия диалогового окна выбора файла

namespace FigmaToWpf
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; }
        public string ActionType { get; private set; } // Свойство для типа действия

        
        // Конструктор с параметрами
        public InputDialog(string title, string commandText, string actionType)
        {
            InitializeComponent(); // Инициализация компонентов
            Title = title; // Установка заголовка окна
            InputTextBox.Text = commandText; // Установка текста команды в текстовое поле

            // Устанавливаем выбранный тип действия, если он задан
            if (!string.IsNullOrEmpty(actionType))
            {
                ActionTypeComboBox.SelectedItem = ActionTypeComboBox.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(item => item.Content.ToString() == actionType);
            }
        }

        private bool isFirstLoad = true; // Флаг для отслеживания первого открытия окна

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

        private void UpdateInputField(string actionType)
        {
            // Устанавливаем ограничения на ввод в зависимости от типа действия
            InputTextBox.MaxLength = actionType switch
            {
                "Голосовой ответ" => 100,
                _ => 100 // Ограничение по умолчанию
            };

            // Очистка полей ввода при смене типа действия, если это не первое открытие окна
            if (!isFirstLoad)
            {
                InputTextBox.Clear(); // Очищаем текстовое поле
            }

            // Обновляем флаг после первого открытия
            isFirstLoad = false;

            // Обновляем видимость кнопки выбора файла
            FileButton.Visibility = actionType == "Открытие файла" ? Visibility.Visible : Visibility.Collapsed;

            // Устанавливаем обработчик ввода в зависимости от типа действия
            InputTextBox.PreviewTextInput -= TextBox_PreviewTextInput_Numbers;
            InputTextBox.PreviewTextInput -= TextBox_PreviewTextInput_Russian;

            if (actionType == "Голосовой ответ")
            {
                InputTextBox.PreviewTextInput += TextBox_PreviewTextInput_Russian; // Разрешаем только русские строчные символы
            }
            else
            {
                InputTextBox.PreviewTextInput += TextBox_PreviewTextInput_Numbers; // Разрешаем только цифры для других типов
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
