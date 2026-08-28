using System.Runtime.InteropServices;
using System.Windows;

namespace Friday
{
    public class MouseService
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        public void MoveMouse(string coordinates)
        {
            try
            {
                string[] parts = coordinates.Split(',');

                if (parts.Length != 2)
                {
                    ShowError("Неверный формат координат. Используйте: X,Y");
                    return;
                }

                // ДОБАВЛЕНО .Trim() для защиты от пробелов
                if (int.TryParse(parts[0].Trim(), out int x) && int.TryParse(parts[1].Trim(), out int y))
                {
                    if (!SetCursorPos(x, y))
                    {
                        ShowError("Не удалось установить позицию курсора");
                    }
                }
                else
                {
                    ShowError("Координаты должны быть целыми числами");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Критическая ошибка: {ex.Message}");
            }
        }
        public void PressMouseButton(string button)
        {
            switch (button)
            {
                case "пкм":
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                    break;
                case "лкм":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;
                case "скм":
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                    break;
                default:
                    ShowError("Ошибка в нажатии клавиши мыши. Используйте: лкм, пкм или скм");
                    break;
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}