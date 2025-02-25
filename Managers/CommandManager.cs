using Friday.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Friday
{
    public class CommandManager
    {
        private string _filePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\commands.txt"));
        private List<Command> _commands;

        public CommandManager()
        {
            _commands = LoadCommands();
        }
        private List<Command> LoadCommands()
        {
            var commands = new List<Command>();

            if (File.Exists(_filePath))
            {
                var lines = File.ReadAllLines(_filePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length == 5)
                    {
                        commands.Add(new Command
                        {
                            Name = parts[0],
                            Description = parts[1],
                            Trigger = parts[2],
                            ExecutionType = parts[3],
                            Action = parts[4]
                        });
                    }
                }
            }

            return commands;
        }
        private void SaveCommands()
        {
            var lines = _commands.Select(c => $"{c.Name}:{c.Description}:{c.Trigger}:{c.ExecutionType}:{c.Action}");
            File.WriteAllLines(_filePath, lines);
        }
        public void AddCommand(Command command)
        {
            _commands.Add(command);
            SaveCommands();
        }
        public void EditCommand(string name, Command newCommand)
        {
            var command = _commands.FirstOrDefault(c => c.Name == name);
            if (command != null)
            {
                command.Name = newCommand.Name;
                command.Description = newCommand.Description;
                command.Trigger = newCommand.Trigger;
                command.ExecutionType = newCommand.ExecutionType;
                command.Action = newCommand.Action;
                SaveCommands();
            }
        }

        public void DeleteCommand(string name)
        {
            var command = _commands.FirstOrDefault(c => c.Name == name);
            if (command != null)
            {
                _commands.Remove(command);
                SaveCommands();
            }
        }

        public List<Command> GetAllCommands()
        {
            return _commands;
        }

        public Command FindCommandByTrigger(string trigger)
        {
            return _commands.FirstOrDefault(c => c.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
        }
    }
}
