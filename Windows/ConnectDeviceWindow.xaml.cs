using Newtonsoft.Json;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;

namespace Friday
{
    public partial class ConnectDeviceWindow : Window
    {
        public ConnectDeviceWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            string deviceName = DeviceNameTextBox.Text;
            string password = PasswordBox.Text;
            string macAddress = App.GetMacAddress();

            if (string.IsNullOrWhiteSpace(deviceName))
            {
                MessageBox.Show("Введите имя устройства", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Введите пароль", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(macAddress))
            {
                MessageBox.Show("Не удалось определить MAC-адрес устройства", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var message = new
                {
                    MAC = macAddress,
                    DeviceName = deviceName,
                    Password = password
                };

                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(message);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://friday-assistant.ru/connect_device", content);

                    // УБРАЛИ response.EnsureSuccessStatusCode(); 
                    // Потому что сервер шлет 400/401/404 вместе с полезным JSON

                    var responseJson = await response.Content.ReadAsStringAsync();

                    // Проверяем, вернул ли сервер хоть какой-то JSON (защита от 500/502 ошибок Nginx)
                    if (string.IsNullOrWhiteSpace(responseJson) || !responseJson.StartsWith("{"))
                    {
                        response.EnsureSuccessStatusCode(); // Вызовет стандартную ошибку, если ответ не JSON
                    }

                    var responseObject = JsonConvert.DeserializeObject<dynamic>(responseJson);

                    if (responseObject != null && responseObject.status == "success")
                    {
                        MessageBox.Show("Устройство успешно подключено", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                        this.DialogResult = true;
                        this.Close();
                    }
                    else
                    {
                        // Теперь мы дойдем сюда и покажем текст "Неверный пароль" или "Устройство не найдено"
                        string errorMsg = responseObject?.message?.ToString() ?? "Неизвестная ошибка сервера";
                        MessageBox.Show(errorMsg, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при подключении устройства: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}