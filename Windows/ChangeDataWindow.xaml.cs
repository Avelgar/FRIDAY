using System.IO;
using System.Text;
using System.Windows;
using Newtonsoft.Json;
using FigmaToWpf;

namespace Friday
{
    public partial class ChangeDataWindow : Window
    {
        public ChangeDataWindow()
        {
            InitializeComponent();
            LoadDataFromFile(); // Загружаем данные при инициализации окна
        }

        private void LoadDataFromFile()
        {
            string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\devisedata.json"));

            // Проверяем, существует ли файл
            if (File.Exists(filePath))
            {
                // Чтение данных из файла
                string jsonData = File.ReadAllText(filePath, Encoding.UTF8);

                // Десериализация данных
                var deviceData = JsonConvert.DeserializeObject<DeviceData>(jsonData);

                // Заполнение текстовых полей
                if (deviceData != null)
                {
                    DeviceNameTextBox.Text = deviceData.DeviceName;
                    PasswordTextBox.Text = deviceData.Password;
                }
            }
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string deviceName = DeviceNameTextBox.Text;
            string password = PasswordTextBox.Text;

            // Создание файла devisedata.json, если он не существует, или очистка его содержимого
            string filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\devisedata.json"));
            // Создаем директорию, если она не существует
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            // Записываем имя устройства и пароль в файл
            var deviceData = new
            {
                DeviceName = deviceName,
                Password = password
            };

            // Сериализация данных в JSON
            string jsonData = JsonConvert.SerializeObject(deviceData, Formatting.Indented);

            // Запись данных в файл (очистка файла, если он существует)
            File.WriteAllText(filePath, jsonData, Encoding.UTF8);
            this.Close();
        }
    }

    // Класс для представления данных устройства
    public class DeviceData
    {
        public string DeviceName { get; set; }
        public string Password { get; set; }
    }
}
