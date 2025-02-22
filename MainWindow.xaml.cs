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
            // Создаем экземпляр RenameService
            RenameService renameService = new RenameService();
            // Передаем его в конструктор VoiceService
            _voiceService = new VoiceService(renameService);
            _voiceService.OnMessageReceived += OnMessageReceived;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_voiceService.ListeningState.IsListening())
            {
                _voiceService.ListeningState.StopListening();
                ListenButton.Content = "Начать слушать";
                ConsoleTextBox.AppendText("Слушание остановлено." + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            }
            else
            {
                _voiceService.ListeningState.StartListening();
                ListenButton.Content = "Остановить слушать";
                _voiceService.StartListening();
                ConsoleTextBox.AppendText("Начинаю слушать..." + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            }
        }

        private void OnMessageReceived(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ConsoleTextBox.AppendText(message + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            });
        }
    }
}
