using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

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
                "Запуск приложения" => 200,
                "Закрытие приложения" => 200,
                "Копирование файла" => 200,
                "Перемещение файла" => 200,
                "Удаление файла" => 200,
                "Создание папки" => 200,
                "Изменение настроек экрана" => 50,
                "Изменение громкости" => 3,
                "Голосовой ответ" => 100,
                _ => 100 // Ограничение по умолчанию
            };

        }

        private void TextBox_PreviewTextInput_Numbers(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$"); // Разрешаем только цифры
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
            if (selectedAction == "Изменение громкости" &&
                (!Regex.IsMatch(InputTextBox.Text, @"^\d{1,3}$") ||
                int.Parse(InputTextBox.Text) < 0 || int.Parse(InputTextBox.Text) > 100))
            {
                MessageBox.Show("Введите корректное значение громкости от 0 до 100.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Сохраняем введенные данные и тип действия
            InputText = InputTextBox.Text;
            ActionType = selectedAction; // Сохраняем выбранный тип действия
            DialogResult = true; // Устанавливаем результат диалога как успешный
            Close(); // Закрываем диалог
        }
    }
}

