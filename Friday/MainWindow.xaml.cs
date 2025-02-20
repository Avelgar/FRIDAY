using Friday;
using System;
using System.Windows;

namespace Friday
{
    public partial class MainWindow : Window
    {
        private VoiceService voiceService;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                voiceService = new VoiceService();
                voiceService.OnMessageReceived += UpdateOutputTextBox;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при инициализации: {ex.Message}");
            }
        }

        private void UpdateOutputTextBox(string message)
        {
            // Используем Dispatcher для обновления UI
            Dispatcher.Invoke(() =>
            {
                outputTextBox.AppendText(message + Environment.NewLine);
            });
        }

        private async void startButton_Click(object sender, RoutedEventArgs e)
        {
            await voiceService.StartListening();
        }
    }
}