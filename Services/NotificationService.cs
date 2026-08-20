using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace Friday
{
    public class NotificationService
    {
        public void SendNotification(string text)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(async () =>
            {
                NotifyIcon notifyIcon = new NotifyIcon();

                notifyIcon.ShowBalloonTip(3000, "Friday", text, ToolTipIcon.Info);

                await Task.Delay(4000);

                notifyIcon.Visible = false;
                notifyIcon.Icon = null;
                notifyIcon.Dispose();
            });
        }
    }
}