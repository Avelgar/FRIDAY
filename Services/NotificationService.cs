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
            if (string.IsNullOrWhiteSpace(text)) return;

            System.Windows.Application.Current.Dispatcher.Invoke(async () =>
            {
                try
                {
                    using (NotifyIcon notifyIcon = new NotifyIcon())
                    {
                        // ОБЯЗАТЕЛЬНО для Win 10/11: иконка и видимость
                        notifyIcon.Icon = SystemIcons.Information;
                        notifyIcon.Visible = true;
                        notifyIcon.Text = "Friday Assistant";

                        notifyIcon.ShowBalloonTip(3500, "Friday", text, ToolTipIcon.Info);

                        await Task.Delay(4000);

                        notifyIcon.Visible = false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка показа уведомления: {ex.Message}");
                }
            });
        }
    }
}