using System.Collections.Generic;

namespace Friday.Managers
{
    public class Command
    {
        public int Id { get; set; }  // Добавим Id
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Actions { get; set; } = new List<string>(); // Список действий

        public Command() { }

        public Command(int id, string name, string description, List<string> actions)
        {
            Id = id;
            Name = name;
            Description = description;
            Actions = actions;
        }
    }
}
