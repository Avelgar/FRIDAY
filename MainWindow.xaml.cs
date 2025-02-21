using System;
using System.Windows;
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

        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            // Логика переключения состояния прослушивания
            if (_voiceService.ListeningState.IsListening())
            {
                _voiceService.ListeningState.StopListening();
                ListenButton.Content = "Начать слушать";
                // Выводим сообщение в консоль о том, что слушание остановлено
                ConsoleTextBox.AppendText("Слушание остановлено." + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            }
            else
            {
                _voiceService.ListeningState.StartListening();
                ListenButton.Content = "Остановить слушать";

                // Запускаем прослушивание речи в VoiceService
                _voiceService.StartListening(); // Важно запустить процесс прослушивания

                // Выводим сообщение в консоль о начале прослушивания
                ConsoleTextBox.AppendText("Начинаю слушать..." + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
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
