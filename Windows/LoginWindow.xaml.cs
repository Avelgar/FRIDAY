using System;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using FigmaToWpf;
using Newtonsoft.Json;

namespace Friday
{
    public partial class LoginWindow : Window
    {
        public string Username { get; private set; }

        public LoginWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string login = LoginTextBox.Text.Trim();
            string password = PasswordBox.Text.Trim();

            // Валидация
            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                ShowError("Все поля обязательны для заполнения");
                return;
            }

            try
            {
                var loginData = new
                {
                    login,
                    password,
                    mac = MainWindow.GetMacAddress() // Добавляем MAC адрес устройства
                };

                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(loginData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("http://blue.fnode.me:25550/login", content);
                    var responseString = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<dynamic>(responseString);

                    if (response.IsSuccessStatusCode)
                    {
                        // Обновляем главное окно
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                            if (mainWindow != null)
                            {
                                mainWindow.ShowUserButton(responseObject.user_login.ToString());
                                mainWindow.ConsoleTextBox.AppendText($"Вход выполнен: {responseObject.user_login}" + Environment.NewLine);
                            }
                        });

                        this.DialogResult = true;
                        this.Close();
                    }
                    else
                    {
                        ShowError(responseObject.message?.ToString() ?? "Ошибка входа");
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