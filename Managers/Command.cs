using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Friday.Managers
{
    public class Command
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Trigger { get; set; }
        public string ExecutionType { get; set; }
        public string Action { get; set; }
        public Command() { }

        public Command(string name, string description, string trigger, string executionType, string action)
        {
            Name = name;
            Description = description;
            Trigger = trigger;
            ExecutionType = executionType;
            Action = action;
        }
    }
}
