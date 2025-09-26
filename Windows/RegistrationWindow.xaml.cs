using FigmaToWpf;
using System.IO;
using System.Text;
using System.Windows;
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
        }
        public string DeviceName => DeviceNameTextBox.Text;
        public string Password => PasswordTextBox.Text;


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

        public static string GetMacAddress()
        {
            return App.GetMacAddress();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}