using System;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using FigmaToWpf;
using Newtonsoft.Json;
using System.Windows.Documents;

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
                    mac = MainWindow.GetMacAddress()
                };

                using (var client = new HttpClient())
                {
                    var json = JsonConvert.SerializeObject(loginData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://friday-assistant.ru/login", content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        // Проверяем, является ли ответ валидным JSON
                        if (IsValidJson(responseString))
                        {
                            var responseObject = JsonConvert.DeserializeObject<dynamic>(responseString);

                            // Проверяем наличие статуса в ответе
                            if (responseObject.status != null && responseObject.status.ToString() == "success")
                            {
                                // Проверяем наличие user_login в ответе
                                if (responseObject.user_login != null)
                                {
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
                                    ShowError("Ошибка: в ответе отсутствует user_login");
                                }
                            }
                            else
                            {
                                ShowError(responseObject.message?.ToString() ?? "Ошибка входа");
                            }
                        }
                        else
                        {
                            ShowError($"Неожиданный ответ от сервера: {responseString}");
                        }
                    }
                    else
                    {
                        ShowError($"Ошибка сервера: {response.StatusCode}");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                ShowError($"Ошибка соединения: {ex.Message}");
            }
            catch (JsonException ex)
            {
                ShowError($"Ошибка обработки ответа: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowError($"Произошла ошибка: {ex.Message}");
            }
        }

        // Вспомогательный метод для проверки валидности JSON
        private bool IsValidJson(string strInput)
        {
            if (string.IsNullOrWhiteSpace(strInput)) return false;

            strInput = strInput.Trim();
            if ((strInput.StartsWith("{") && strInput.EndsWith("}")) ||
                (strInput.StartsWith("[") && strInput.EndsWith("]")))
            {
                try
                {
                    var obj = JsonConvert.DeserializeObject(strInput);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        private void ForgotPasswordLink_Click(object sender, RoutedEventArgs e)
        {
            // Закрываем окно логина и открываем окно восстановления пароля
            var changePasswordWindow = new ChangePasswordWindow();
            changePasswordWindow.Show();
            this.Close();
        }
        private void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }
    }
}