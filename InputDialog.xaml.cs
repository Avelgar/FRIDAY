using System.Windows;

namespace FigmaToWpf
{
    public partial class InputDialog : Window
    {
        public string InputText
        {
            get { return InputTextBox.Text; }
            set { InputTextBox.Text = value; }
        }


        public InputDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            this.DialogResult = true; // Устанавливаем результат диалога в true
            this.Close(); // Закрываем окно
        }
    }

}
