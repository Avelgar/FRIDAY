using Friday.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Friday
{
    public class CommandManager
    {
        private string FilePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\commands.txt"));
        private List<Command> _commands;

        public CommandManager()
        {
            LoadCommands();
        }

        public void AddCommand(string name, string description, List<ActionItem> actions, bool isPassword)
        {
            int id = _commands.Count > 0 ? _commands.Max(c => c.Id) + 1 : 1; // Генерация уникального ID
            var command = new Command(id, name, description, actions, isPassword);
            _commands.Add(command);
            SaveCommands();
        }

        public void DeleteCommand(string commandName)
        {
            var commandToRemove = _commands.FirstOrDefault(c => c.Name == commandName);
            if (commandToRemove != null)
            {
                _commands.Remove(commandToRemove);
                SaveCommands();
            }
        }

        public void EditCommand(int id, string newName, string newDescription, List<ActionItem> newActions, bool isPassword)
        {
            var commandToEdit = _commands.FirstOrDefault(c => c.Id == id);
            if (commandToEdit != null)
            {
                commandToEdit.Name = newName;
                commandToEdit.Description = newDescription;
                commandToEdit.Actions = newActions;
                commandToEdit.IsPassword = isPassword;
                SaveCommands();
            }
        }

        public List<Command> GetCommands()
        {
            return _commands;
        }

        public Command FindCommandByTrigger(string trigger)
        {
            return _commands.FirstOrDefault(c => c.Name.Equals(trigger, StringComparison.OrdinalIgnoreCase));
        }

        private void LoadCommands()
        {
            if (!File.Exists(FilePath))
            {
                using (File.Create(FilePath)) { }
                _commands = new List<Command>();
                return;
            }

            var lines = File.ReadAllLines(FilePath);
            _commands = new List<Command>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');

                if (parts.Length < 4)
                    continue;

                int commandId = int.Parse(parts[0]);
                string name = parts[1];
                string description = parts[2];
                bool isPassword = bool.Parse(parts[3]);

                List<ActionItem> actions = new List<ActionItem>();
                if (parts.Length > 4)
                {
                    var actionsParts = parts[4].Split(';');
                    for (int i = 0; i < actionsParts.Length; i++)
                    {
                        var actionDetails = actionsParts[i].Split(':');
                        if (actionDetails.Length == 3)
                        {
                            int actionId = int.Parse(actionDetails[0]);
                            actions.Add(new ActionItem(actionId, actionDetails[1], actionDetails[2]));
                        }
                    }
                }

                var command = new Command(commandId, name, description, actions, isPassword);
                _commands.Add(command);
            }
        }

        private void SaveCommands()
        {
            using (var writer = new StreamWriter(FilePath))
            {
                foreach (var command in _commands)
                {
                    var actions = string.Join(";", command.Actions.Select(a => $"{a.Id}:{a.ActionType}:{a.ActionText}"));
                    var line = $"{command.Id},{command.Name},{command.Description},{command.IsPassword},{actions}";
                    writer.WriteLine(line);
                }
            }
        }
    }
}
