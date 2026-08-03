using MeowShot.Services;

namespace MeowShot.Windows;

public sealed class SettingsWindow : MainWindow
{
    public SettingsWindow(SettingsService settingsService) : base(settingsService)
    {
    }
}
