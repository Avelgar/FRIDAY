using System;
using System.Windows;
using System.Windows.Forms;
using System.Threading;

namespace Friday
{
    public class KeyboardService
    {
        public void TypeText(string text)
        {
            try
            {
                // Восстановление переносов
                text = text.Replace("\\n", Environment.NewLine)
                           .Replace("```csharp", "")
                           .Replace("```", "")
                           .Trim();

                // Сохраняем текущий буфер обмена
                string originalClipboard = System.Windows.Clipboard.GetText();

                // Копируем текст в буфер обмена
                System.Windows.Clipboard.SetText(text);

                // Даем время на обновление буфера
                Thread.Sleep(200);

                // Отправляем комбинацию Ctrl+V для вставки
                SendKeys.SendWait("^(v)");

                // Восстанавливаем оригинальный буфер
                Thread.Sleep(100);
                System.Windows.Clipboard.SetText(originalClipboard);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка при печати текста: {ex.Message}");
            }
        }
    }
}

