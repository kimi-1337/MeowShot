using System.Drawing;
using Forms = System.Windows.Forms;

namespace MeowShot.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private Action? _notificationClick;

    public TrayService(
        Action capture,
        Action editLast,
        Action settings,
        Action history,
        Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Новый снимок  Ctrl+Shift+S", null, (_, _) => capture());
        menu.Items.Add("Редактировать последний", null, (_, _) => editLast());
        menu.Items.Add("История", null, (_, _) => history());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Настройки", null, (_, _) => settings());
        menu.Items.Add("Выход", null, (_, _) => exit());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "MeowShot — Ctrl+Shift+S",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => capture();
        _notifyIcon.BalloonTipClicked += (_, _) => _notificationClick?.Invoke();
    }

    public void ShowCaptureNotification(string message, Action onClick)
    {
        _notificationClick = onClick;
        _notifyIcon.BalloonTipTitle = "MeowShot";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(5000);
    }

    public void ShowWarning(string message)
    {
        _notificationClick = null;
        _notifyIcon.BalloonTipTitle = "MeowShot";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(6000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
