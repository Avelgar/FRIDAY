namespace Friday
{
    public class BrowserService
    {
        public void OpenLink(string url)
        {
            System.Diagnostics.Process.Start(url);
        }
    }
}