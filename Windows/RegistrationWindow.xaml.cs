using System.Windows;
using System.Windows.Input;

namespace Friday
{
    public partial class RegistrationWindow : Window
    {
        public RegistrationWindow()
        {
            InitializeComponent();
        }
        public string DeviceName => DeviceNameTextBox.Text;
        public string Password => PasswordTextBox.Password;


        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string deviceName = DeviceNameTextBox.Text;
            string password = PasswordTextBox.Password;

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

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}