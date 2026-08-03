using System.Windows;
using System.Windows.Media.Imaging;
using MeowShot.Interop;
using MeowShot.Windows;

namespace MeowShot.Services;

public sealed class CaptureCoordinator : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly List<CaptureOverlayWindow> _overlays = [];
    private bool _active;
    private BitmapSource? _desktopSnapshot;
    private NativeRect _virtualBounds;

    public event Action<BitmapSource>? Captured;

    public CaptureCoordinator(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Start()
    {
        if (_active)
        {
            return;
        }

        try
        {
            _active = true;
            _virtualBounds = ScreenCaptureService.GetVirtualScreenBounds();
            _desktopSnapshot = ScreenCaptureService.CaptureVirtualScreen(_settingsService.Current.IncludeCursor);
            var monitors = ScreenCaptureService.GetMonitors();
            if (monitors.Count == 0)
            {
                throw new InvalidOperationException("Windows не сообщила ни об одном доступном мониторе.");
            }

            var windows = ScreenCaptureService.GetCapturableWindows();
            var session = new CaptureSession();
            session.Finished += bounds => Finish(session, bounds);

            foreach (var monitor in monitors.OrderBy(item => item.IsPrimary ? 0 : 1))
            {
                var monitorImage = ScreenCaptureService.Crop(_desktopSnapshot, monitor.Bounds, _virtualBounds);
                var overlay = new CaptureOverlayWindow(
                    monitor,
                    monitorImage,
                    windows,
                    session,
                    _virtualBounds,
                    _settingsService.Current.CaptureAllMonitorsInFullScreenMode);
                _overlays.Add(overlay);
                overlay.Show();
            }

            _overlays.FirstOrDefault(window => window.IsPrimaryMonitor)?.Activate();
        }
        catch (Exception exception)
        {
            CloseOverlays();
            _active = false;
            MessageBox.Show(exception.Message, "MeowShot — ошибка захвата",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Finish(CaptureSession session, NativeRect? selectedBounds)
    {
        session.Finished -= bounds => Finish(session, bounds);
        CloseOverlays();

        try
        {
            if (selectedBounds.HasValue && _desktopSnapshot is not null)
            {
                var result = ScreenCaptureService.Crop(_desktopSnapshot, selectedBounds.Value, _virtualBounds);
                Captured?.Invoke(result);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "MeowShot — ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _desktopSnapshot = null;
            _active = false;
        }
    }

    private void CloseOverlays()
    {
        foreach (var overlay in _overlays.ToArray())
        {
            overlay.CloseSafely();
        }

        _overlays.Clear();
    }

    public void Dispose()
    {
        CloseOverlays();
        _desktopSnapshot = null;
        _active = false;
    }
}
