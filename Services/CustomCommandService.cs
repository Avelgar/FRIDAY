using Friday.Managers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vosk;

namespace Friday
{
    public class CustomCommandService
    {
        public static void ExecuteCommand(Command command)
        {
            var appProcessService = new AppProcessService();
            switch (command.ExecutionType.ToLower())
            {
                case "say":
                    break;
                case "open file":
                    appProcessService.OpenFile(command.Action);
                    break;
                default:
                    throw new NotSupportedException($"Тип выполнения '{command.ExecutionType}' не поддерживается.");
            }
        }
    }
}
