using System;
using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;

public class BluetoothService
{
    // Импорт функции BluetoothSetRadioState из Bthprops.dll
    [DllImport("Bthprops.dll", SetLastError = true)]
    private static extern int BluetoothSetRadioState(int fEnable);

    public void SetBluetoothState(string command)
    {
        // Нормализация команды (регистронезависимая проверка)
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

        // Вызов системной функции (1 = включить, 0 = выключить)
        int result = BluetoothSetRadioState(enable ? 1 : 0);

        // Проверка результата
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