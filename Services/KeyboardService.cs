using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Friday
{
    public class KeyboardService
    {

        [DllImport("user32.dll")]
        internal static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input { public uint type; public InputUnion u; } // Добавили internal

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion { [FieldOffset(0)] public KeyboardInput ki; } // Добавили internal

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; } // Добавили internal
                                                                                                                                                    // Делаем метод асинхронным, чтобы не блокировать интерфейс программы

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_UNICODE = 0x0004;

        public void TypeTextDirectly(string text)
        {
            foreach (char c in text)
            {
                SendUnicodeChar(c);
            }
            // Нажимаем Enter после ввода пароля
            SimulateKeyPress(0x0D);
        }

        private void SendUnicodeChar(char c)
        {
            Input[] inputs = new Input[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wScan = c;
            inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wScan = c;
            inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

            SendInput(2, inputs, Marshal.SizeOf(typeof(Input)));
        }

        public void SimulateKeyPress(ushort vkCode)
        {
            Input[] inputs = new Input[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vkCode;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = vkCode;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(2, inputs, Marshal.SizeOf(typeof(Input)));
        }

        public async void TypeText(string text)
        {
            try
            {
                // Восстановление переносов
                text = text.Replace("\\n", Environment.NewLine)
                           .Replace("```csharp", "")
                           .Replace("```", "")
                           .Trim();

                string originalClipboard = string.Empty;

                // 1. Работаем с буфером обмена СТРОГО в главном UI-потоке
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    originalClipboard = System.Windows.Clipboard.ContainsText()
                        ? System.Windows.Clipboard.GetText()
                        : string.Empty;

                    System.Windows.Clipboard.SetText(text);
                });

                // 2. Ждем в фоне (не подвешивая программу), чтобы система успела обновить буфер
                await Task.Delay(200);

                // 3. Отправляем комбинацию Ctrl+V (правильный синтаксис ^v)
                SendKeys.SendWait("^v");

                await Task.Delay(100);

                // 4. Возвращаем оригинальный текст обратно в буфер в UI-потоке
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(originalClipboard))
                    {
                        System.Windows.Clipboard.SetText(originalClipboard);
                    }
                    else
                    {
                        System.Windows.Clipboard.Clear();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка при печати текста: {ex.Message}");
            }
        }
    }
}