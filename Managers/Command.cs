namespace Friday.Managers
{
    public class Command
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<ActionItem> Actions { get; set; } = new List<ActionItem>();
        public bool IsPassword { get; set; }

        public Command() { }

        public Command(int id, string name, string description, List<ActionItem> actions, bool isPassword)
        {
            Id = id;
            Name = name;
            Description = description;
            Actions = actions;
            IsPassword = isPassword;
        }
    }

    public class ActionItem
    {
        public int Id { get; set; }
        public string ActionType { get; set; }
        public string ActionText { get; set; }

        public ActionItem() { }

        public ActionItem(int id, string actionType, string actionText)
        {
            Id = id;
            ActionType = actionType;
            ActionText = actionText;
        }
    }
}
