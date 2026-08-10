using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.IO;
using System.Windows.Input;

namespace Friday
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }
        private void Close_Click(object sender, RoutedEventArgs e) { this.Close(); }
        private void ForgotPasswordLink_Click(object sender, RoutedEventArgs e) { new ChangePasswordWindow().Show(); this.Close(); }
        private void ShowError(string message) { ErrorTextBlock.Text = message; ErrorTextBlock.Visibility = Visibility.Visible; }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string login = LoginTextBox.Text.Trim();
            string password = PasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password)) { ShowError("Все поля обязательны"); return; }

            try
            {
                var loginData = new { login, password };
                using (var client = new HttpClient())
                {
                    var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync("https://friday-assistant.ru/login", content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var responseObject = JsonConvert.DeserializeObject<dynamic>(responseString);
                        if (responseObject.status == "success" && responseObject.token != null)
                        {
                            string token = responseObject.token.ToString();
                            string userLogin = responseObject.user_login.ToString();

                            // Передаем логин и токен в App, он сам сохранит файл и запустит синхронизацию
                            ((App)Application.Current).UpdateAccountData(userLogin, token);

                            this.DialogResult = true;
                            this.Close();
                        }
                        else ShowError(responseObject.message?.ToString() ?? "Ошибка входа");
                    }
                    else ShowError($"Ошибка сервера: {response.StatusCode}");
                }
            }
            catch (Exception ex) { ShowError($"Произошла ошибка: {ex.Message}"); }
        }
    }
}