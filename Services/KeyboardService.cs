using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Friday
{
    public class KeyboardService
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public KeyboardInput ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        public async void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                // Очищаем от артефактов форматирования нейросети
                text = text.Replace("\\n", Environment.NewLine)
                           .Replace("```csharp", "")
                           .Replace("```", "")
                           .Trim();

                // Убираем обрамляющие кавычки, если ИИ их добавил
                if (text.Length >= 2 && text.StartsWith("\"") && text.EndsWith("\""))
                {
                    text = text.Substring(1, text.Length - 2);
                }

                // Небольшая пауза, чтобы целевое окно успело принять фокус
                await Task.Delay(150);

                // Печатаем посимвольно напрямую через Юникод
                foreach (char c in text)
                {
                    if (c == '\r') continue;
                    if (c == '\n')
                    {
                        SimulateKeyPress(0x0D); // Нажатие Enter
                    }
                    else
                    {
                        SendUnicodeChar(c);
                    }
                    await Task.Delay(5);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при печати текста: {ex.Message}");
            }
        }

        private void SendUnicodeChar(char c)
        {
            Input[] inputs = new Input[2];

            inputs[0] = new Input
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KeyboardInput
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            inputs[1] = new Input
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KeyboardInput
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(2, inputs, Marshal.SizeOf(typeof(Input)));
        }

        public void SimulateKeyPress(ushort vkCode)
        {
            Input[] inputs = new Input[2];

            inputs[0] = new Input
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KeyboardInput
                    {
                        wVk = vkCode,
                        wScan = 0,
                        dwFlags = 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            inputs[1] = new Input
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KeyboardInput
                    {
                        wVk = vkCode,
                        wScan = 0,
                        dwFlags = KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(2, inputs, Marshal.SizeOf(typeof(Input)));
        }
    }
}