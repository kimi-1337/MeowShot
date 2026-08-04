using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace MeowShot.Services;

public sealed class NativeNotificationService : IDisposable
{
    private readonly Action<string, string?> _invoked;
    private AppNotificationManager? _manager;
    private bool _registered;

    public NativeNotificationService(Action<string, string?> invoked)
    {
        _invoked = invoked;
    }

    public bool Register()
    {
        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                return false;
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "meowshot-icon.png");
            _manager = AppNotificationManager.Default;
            _manager.NotificationInvoked += OnNotificationInvoked;
            _manager.Register("MeowShot", new Uri(Path.GetFullPath(iconPath)));
            _registered = true;
            return true;
        }
        catch
        {
            _manager = null;
            _registered = false;
            return false;
        }
    }

    public bool ShowCapture(string captureId, int width, int height, string? savedPath)
    {
        if (!_registered || _manager is null)
        {
            return false;
        }

        try
        {
            var iconUri = new Uri(Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "Assets", "meowshot-icon.png")));
            var details = savedPath is null
                ? $"{width} × {height} · PNG без потерь"
                : $"{width} × {height} · сохранён в {Path.GetFileName(savedPath)}";

            var notification = new AppNotificationBuilder()
                .AddArgument("action", "edit")
                .AddArgument("captureId", captureId)
                .AddText("Снимок скопирован в буфер обмена")
                .AddText(details)
                .SetAppLogoOverride(iconUri, AppNotificationImageCrop.Circle, "MeowShot")
                .AddButton(new AppNotificationButton("Редактировать")
                    .AddArgument("action", "edit")
                    .AddArgument("captureId", captureId))
                .AddButton(new AppNotificationButton("Сохранить как…")
                    .AddArgument("action", "save")
                    .AddArgument("captureId", captureId))
                .BuildNotification();
            notification.Expiration = DateTimeOffset.Now.AddHours(24);
            notification.ExpiresOnReboot = true;
            notification.Tag = captureId[..Math.Min(16, captureId.Length)];
            notification.Group = "captures";
            _manager.Show(notification);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        args.Arguments.TryGetValue("action", out var action);
        args.Arguments.TryGetValue("captureId", out var captureId);
        _invoked(string.IsNullOrWhiteSpace(action) ? "edit" : action, captureId);
    }

    public void Dispose()
    {
        if (!_registered || _manager is null)
        {
            return;
        }

        try
        {
            _manager.NotificationInvoked -= OnNotificationInvoked;
            _manager.Unregister();
        }
        catch
        {
            // The application is already exiting, so cleanup cannot be retried safely.
        }

        _registered = false;
        _manager = null;
    }
}
