using System;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using FigmaToWpf;
using Newtonsoft.Json;

namespace Friday
{
    public partial class RegisterWindow : Window
    {
        public RegisterWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailTextBox.Text.Trim();
            string login = LoginTextBox.Text.Trim();
            string password1 = PasswordBox1.Text.Trim();
            string password2 = PasswordBox2.Text.Trim();

            // Валидация
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(login) ||
                string.IsNullOrEmpty(password1) || string.IsNullOrEmpty(password2))
            {
                ShowError("Все поля обязательны для заполнения");
                return;
            }

            if (password1 != password2)
            {
                ShowError("Пароли не совпадают");
                return;
            }

            if (password1.Length < 6)
            {
                ShowError("Пароль должен содержать минимум 6 символов");
                return;
            }

            try
            {
                var registerData = new
                {
                    email,
                    login,
                    password = password1,
                    mac = MainWindow.GetMacAddress() 
                };

                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(registerData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://friday-assistant.ru/register", content);
                    var responseString = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<dynamic>(responseString);

                    if (response.IsSuccessStatusCode)
                    {

                        MessageBox.Show("Регистрация прошла успешно!", "Успех",
                                      MessageBoxButton.OK, MessageBoxImage.Information);
                        this.DialogResult = true;
                        this.Close();
                    }
                    else
                    {
                        ShowError(responseObject.message?.ToString() ?? "Ошибка регистрации");
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