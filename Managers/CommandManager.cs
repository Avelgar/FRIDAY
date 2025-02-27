using Friday.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
                    if (parts.Length >= 3) // Изменено для работы с новыми свойствами
                    {
                        var actions = new List<string>();
                        // Предполагаем, что действия могут быть перечислены через запятую
                        if (parts.Length > 3)
                        {
                            actions = parts[3].Split(',').ToList(); // Разделяем действия
                        }

                        commands.Add(new Command
                        {
                            Id = commands.Count + 1, // Генерируем ID на основе текущего количества команд
                            Name = parts[0],
                            Description = parts[1],
                            Actions = actions
                        });
                    }
                }
            }

            return commands;
        }

        private void SaveCommands()
        {
            var lines = _commands.Select(c => $"{c.Name}|{c.Description}|{string.Join(",", c.Actions)}");
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
                command.Actions = newCommand.Actions; // Обновляем действия
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
            return _commands.FirstOrDefault(c => c.Name.Equals(trigger, StringComparison.OrdinalIgnoreCase));
        }
    }
}
