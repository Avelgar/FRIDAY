using System.Drawing;
using System.Windows.Forms;

namespace Friday
{
    public class NotificationService
    {
        public void SendNotification(string text)
        {
            NotifyIcon notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Information;
            notifyIcon.Visible = true;
            SettingManager _settingManager = new SettingManager();
            notifyIcon.ShowBalloonTip(3000, _settingManager.Setting.AssistantName, text, ToolTipIcon.Info);
        }
    }
}
