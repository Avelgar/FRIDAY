using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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
