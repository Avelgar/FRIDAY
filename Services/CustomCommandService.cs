using Friday.Managers;
using System;
using System.Collections.Generic;

namespace Friday
{
    public class CustomCommandService
    {
        public static void ExecuteCommand(Command command)
        {
            //var appProcessService = new AppProcessService();

            //// Проверяем, что команда не null
            //if (command == null)
            //{
            //    throw new ArgumentNullException(nameof(command), "Команда не может быть null.");
            //}

            //// Обрабатываем действия, которые могут быть связаны с командой
            //foreach (var action in command.Actions)
            //{
            //    switch (action.ToLower())
            //    {
            //        case "say":
            //            // Здесь можно добавить логику для произнесения текста
            //            Console.WriteLine($"Говорим: {command.Description}");
            //            break;

            //        case "open file":
            //            // Здесь нужно указать, какой файл открывать
            //            appProcessService.OpenFile("путь_к_файлу"); // Замените на реальный путь
            //            break;

            //        default:
            //            throw new NotSupportedException($"Действие '{action}' не поддерживается.");
            //    }
            //}
        }
    }
}
