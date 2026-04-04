using System.ComponentModel;
using System.Runtime.InteropServices;

public class BluetoothService
{
    [DllImport("Bthprops.dll", SetLastError = true)]
    private static extern int BluetoothSetRadioState(int fEnable);

    public void SetBluetoothState(string command)
    {
        string normalizedCommand = command?.Trim().ToLower() ?? string.Empty;

        bool enable;
        switch (normalizedCommand)
        {
            case "включить":
                enable = true;
                break;
            case "выключить":
                enable = false;
                break;
            default:
                throw new ArgumentException(
                    "Некорректная команда. Допустимые значения: 'включить', 'выключить'.",
                    nameof(command)
                );
        }

        int result = BluetoothSetRadioState(enable ? 1 : 0);

        if (result == 0)
        {
            int errorCode = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                errorCode,
                $"Ошибка при изменении состояния Bluetooth. Код ошибки: {errorCode}"
            );
        }
    }
}