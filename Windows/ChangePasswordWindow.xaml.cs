using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace Friday
{
    public partial class ChangePasswordWindow : Window
    {
        public ChangePasswordWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void OpenLoginWindowLink_Click(object sender, RoutedEventArgs e)
        {
            // Закрываем окно восстановления пароля и открываем окно логина
            var loginWindow = new LoginWindow();
            loginWindow.Show();
            this.Close();
        }

        private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailTextBox.Text.Trim();

            // Валидация email
            if (string.IsNullOrEmpty(email))
            {
                ShowError("Пожалуйста, введите email");
                return;
            }

            if (!email.Contains("@"))
            {
                ShowError("Пожалуйста, введите корректный email");
                return;
            }

            try
            {
                var recoveryData = new
                {
                    email = email
                };

                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(recoveryData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://friday-assistant.ru/recover-password", content);
                    var responseString = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<dynamic>(responseString);

                    if (response.IsSuccessStatusCode)
                    {
                        MessageBox.Show("Инструкции по восстановлению пароля отправлены на ваш email",
                                        "Восстановление пароля",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);

                        // Возвращаемся к форме входа
                        OpenLoginWindowLink_Click(null, null);
                    }
                    else
                    {
                        ShowError(responseObject.message?.ToString() ?? "Ошибка восстановления пароля");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                ShowError($"Ошибка соединения: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowError($"Произошла ошибка: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }
    }
}