using System.Diagnostics;

namespace Friday
{
    public class BrowserService
    {
        public void OpenLink(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}