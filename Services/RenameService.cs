using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Friday
{
    public class RenameService
    {
        private string _botName = "пятница";

        public string BotName
        {
            get => _botName;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _botName = value;
                }
            }
        }
    }
}

