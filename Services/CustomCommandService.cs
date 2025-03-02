using Friday.Managers;
using System;
using System.Collections.Generic;

namespace Friday
{
    public class CustomCommandService
    {
        public static void ExecuteCommand(Command command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command), "Команда не может быть null.");
            }

            var appProcessService = new AppProcessService();

            // Обрабатываем действия команды
            foreach (var action in command.Actions)
            {
                switch (action.ActionType.ToLower())
                {
                    case "запуск приложения":
                        appProcessService.OpenFile(action.ActionText);
                        break;

                    default:
                        throw new NotSupportedException($"Действие '{action.ActionType}' не поддерживается.");
                }
            }
        }
    }
}
