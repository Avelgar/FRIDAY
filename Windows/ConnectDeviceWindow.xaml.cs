using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using Newtonsoft.Json;

namespace FigmaToWpf
{
    public partial class ConnectDeviceWindow : Window
    {
        public ConnectDeviceWindow()
        {
            InitializeComponent();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            string deviceName = DeviceNameTextBox.Text;
            string password = PasswordBox.Text;
            string macAddress = GetMacAddress();

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
                    response.EnsureSuccessStatusCode();

                    var responseJson = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<dynamic>(responseJson);

                    if (responseObject.status == "success")
                    {
                        MessageBox.Show("Устройство успешно подключено", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                        this.DialogResult = true;
                        this.Close();
                    }
                    else
                    {
                        MessageBox.Show(responseObject.message.ToString(), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при подключении устройства: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static string GetMacAddress()
        {
            // Получаем все сетевые интерфейсы
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            // Ищем первый активный интерфейс с физическим адресом
            foreach (NetworkInterface networkInterface in networkInterfaces)
            {
                // Пропускаем интерфейсы, которые не работают (не активны) или не имеют физического адреса
                if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                    !string.IsNullOrEmpty(networkInterface.GetPhysicalAddress().ToString()))
                {
                    // Получаем MAC-адрес и форматируем его с дефисами
                    string macAddress = networkInterface.GetPhysicalAddress().ToString();
                    if (macAddress.Length == 12) // Стандартная длина MAC без разделителей
                    {
                        return string.Join("-", Enumerable.Range(0, 6)
                            .Select(i => macAddress.Substring(i * 2, 2)));
                    }
                    return macAddress; // Если уже есть разделители, возвращаем как есть
                }
            }

            return string.Empty; // Возвращаем пустую строку, если не нашли MAC-адрес
        }
    }
}