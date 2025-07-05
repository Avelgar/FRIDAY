using System.Windows;
using System;
using System.Windows.Forms;

namespace Friday
{

    public class KeyboardService
    {
        public void TypeText(string text)
        {
            // Экранируем специальные символы для SendKeys
            string escapedText = text
                .Replace("(", "{(}")
                .Replace(")", "{)}")
                .Replace("+", "{+}")
                .Replace("^", "{^}")
                .Replace("%", "{%}")
                .Replace("~", "{~}")
                .Replace("[", "{[}")
                .Replace("]", "{]}");

            SendKeys.SendWait(escapedText);
        }
    }
}