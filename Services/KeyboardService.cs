using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Friday
{
    public class KeyboardService
    {

        [DllImport("user32.dll")]
        internal static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion { [FieldOffset(0)] public KeyboardInput ki; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_UNICODE = 0x0004;

        public void TypeTextDirectly(string text)
        {
            foreach (char c in text)
            {
                SendUnicodeChar(c);
            }
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
                text = text.Replace("\\n", Environment.NewLine)
                           .Replace("```csharp", "")
                           .Replace("```", "")
                           .Trim();

                string originalClipboard = string.Empty;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    originalClipboard = System.Windows.Clipboard.ContainsText()
                        ? System.Windows.Clipboard.GetText()
                        : string.Empty;

                    System.Windows.Clipboard.SetText(text);
                });

                await Task.Delay(200);

                SendKeys.SendWait("^v");

                await Task.Delay(100);

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