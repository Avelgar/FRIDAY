using Friday.Managers;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Friday.Windows
{
    public partial class AppPathsWindow : Window
    {
        private ObservableCollection<AppItem> _apps;

        public AppPathsWindow()
        {
            InitializeComponent();
            LoadApps();
        }

        private void LoadApps()
        {
            _apps = new ObservableCollection<AppItem>(AppPathManager.LoadApps());
            AppsItemsControl.ItemsSource = _apps;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BrowseApp_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                // ДОБАВЛЕНА ПОДДЕРЖКА ЯРЛЫКОВ (.lnk)
                Filter = "Программы и Ярлыки (*.exe;*.lnk)|*.exe;*.lnk|Все файлы (*.*)|*.*",
                Title = "Выберите приложение или его ярлык на рабочем столе"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                AppPathTextBox.Text = openFileDialog.FileName;

                if (AppNameTextBox.Text == "Название (напр. Dota 2)" || string.IsNullOrWhiteSpace(AppNameTextBox.Text))
                {
                    AppNameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                }
            }
        }

        private void AddApp_Click(object sender, RoutedEventArgs e)
        {
            string name = AppNameTextBox.Text.Trim();
            string path = AppPathTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name) || name == "Название (напр. Dota 2)" ||
                string.IsNullOrWhiteSpace(path) || path == "Путь к .exe файлу")
            {
                MessageBox.Show("Заполните корректно название и путь!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newApp = new AppItem { Name = name, Path = path };
            _apps.Add(newApp);
            AppPathManager.SaveApps(_apps.ToList());

            AppNameTextBox.Text = "Название (напр. Dota 2)";
            AppPathTextBox.Text = "Путь к .exe файлу";
        }

        private void DeleteApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is AppItem appToDelete)
            {
                _apps.Remove(appToDelete);
                AppPathManager.SaveApps(_apps.ToList());
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb.Text == "Название (напр. Dota 2)" || tb.Text == "Путь к .exe файлу")
            {
                tb.Text = "";
            }
        }
    }
}