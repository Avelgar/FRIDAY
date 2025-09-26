using System;
using System.Windows;
using System.Runtime.InteropServices;

namespace Friday
{
    public class MouseService
    {
        // Импорт функции для установки позиции курсора
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        // Константы для событий мыши
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        // Новый метод для перемещения мыши
        public void MoveMouse(string coordinates)
        {
            try
            {
                // Разделяем входную строку по запятой
                string[] parts = coordinates.Split(',');

                // Проверяем количество частей
                if (parts.Length != 2)
                {
                    ShowError("Неверный формат координат. Используйте: X,Y");
                    return;
                }

                // Парсим координаты
                if (int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                {
                    // Устанавливаем позицию курсора
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

        // Вспомогательный метод для показа ошибок
        private void ShowError(string message)
        {
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}