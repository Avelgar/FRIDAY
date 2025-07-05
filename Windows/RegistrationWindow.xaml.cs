using FigmaToWpf;
using System.IO;
using System.Text;
using System.Windows;
using WebSocketSharp;
using System.Net.NetworkInformation;
using Newtonsoft.Json;
using static Friday.App;
using System.Security.Cryptography;
using System;

namespace Friday
{
    public partial class RegistrationWindow : Window
    {
        public RegistrationWindow()
        {
            InitializeComponent();
            ((App)Application.Current).OnMessageReceived += HandleWebSocketMessage;
            ((App)Application.Current).IncrementWindowCount();
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string deviceName = DeviceNameTextBox.Text;
            string password = PasswordTextBox.Text;

            if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Пожалуйста, заполните все поля.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var InstalledApplications = ((App)Application.Current).InstalledApplications;

            var registrationData = new { MAC = GetMacAddress(), DeviceName = deviceName, Password = password, Programs = InstalledApplications };
            ((App)Application.Current).SendWebSocketMessage(registrationData);
        }

        private void HandleWebSocketMessage(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (message.Contains("Данные успешно обработаны!"))
                {
                    try
                    {
                        var response = JsonConvert.DeserializeObject<dynamic>(message);
                        UpdateDeviceDataFile(DeviceNameTextBox.Text, PasswordTextBox.Text);
                    }
                    catch
                    {
                        // Обработка некорректного JSON
                    }
                }
                else if (message.Contains("\"status\":\"error\""))
                {
                    try
                    {
                        var response = JsonConvert.DeserializeObject<dynamic>(message);
                        MessageBox.Show(response.message.ToString(), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch
                    {
                        // Обработка некорректного JSON
                    }
                }
            });
        }

        private void UpdateDeviceDataFile(string deviceName, string password)
        {
            ((App)Application.Current).UpdateDeviceDataFile(deviceName, password);
        }

        public static string GetMacAddress()
        {
            return App.GetMacAddress();
        }

        protected override void OnClosed(EventArgs e)
        {
            ((App)Application.Current).OnMessageReceived -= HandleWebSocketMessage;
            ((App)Application.Current).DecrementWindowCount();
            base.OnClosed(e);
        }
    }
}