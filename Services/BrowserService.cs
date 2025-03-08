using System.Diagnostics;

namespace Friday
{
    public class BrowserService
    {
        public void OpenLink(string url)
        {
            if (!url.Contains("https://"))
            {
                url = "https://" + url;
            }
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}