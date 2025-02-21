using System;
using System.Windows;
using System.Windows.Input;
using Friday;

namespace FigmaToWpf
{
    public partial class MainWindow : Window
    {
        private VoiceService _voiceService;

        public MainWindow()
        {
            InitializeComponent();
            _voiceService = new VoiceService();
            _voiceService.OnMessageReceived += OnMessageReceived; // Подключаем обработчик
        }

        // Свернуть окно
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // Развернуть/Восстановить окно
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        // Закрыть приложение
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // Обработчик для кнопки "Начать слушать"
        private async void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            // Переключаем состояние
            if (_voiceService.IsListening)
            {
                _voiceService.StopListening();
                ListenButton.Content = "Начать слушать";
            }
            else
            {
                await _voiceService.StartListening();
                ListenButton.Content = "Остановить слушать";
            }
        }

        // Обработчик события получения сообщения от VoiceService
        private void OnMessageReceived(string message)
        {
            // Выводим сообщение в консольное окно
            Dispatcher.Invoke(() =>
            {
                ConsoleTextBox.AppendText(message + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            });
        }
    }
}
