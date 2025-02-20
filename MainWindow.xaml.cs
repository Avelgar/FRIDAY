using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Friday
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
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