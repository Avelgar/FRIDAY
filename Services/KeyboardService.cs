using System.Windows.Forms;

namespace Friday
{
    public class KeyboardService
    {
        public void TypeText(string text) 
        {
            SendKeys.SendWait(text);
        }
    }
}
