using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using MeowShot.Services;
using MeowShot.Windows;
using Microsoft.Win32;

namespace MeowShot;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private HotkeyService? _hotkeyService;
    private TrayService? _trayService;
    private CaptureCoordinator? _captureCoordinator;
    private NativeNotificationService? _notificationService;
    private SettingsWindow? _settingsWindow;
    private BitmapSource? _lastCapture;
    private bool _isExiting;

    public static new App Current => (App)Application.Current;
    public SettingsService SettingsService { get; private set; } = null!;
    public AppSettings Settings => SettingsService.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\MeowShot.ByKimi.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("MeowShot уже запущен. Ищите значок программы в трее.", "MeowShot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        SettingsService = new SettingsService();
        SettingsService.Load();
        StartupService.Apply(Settings.StartWithWindows);
        NotificationCacheService.CleanupExpired();

        _trayService = new TrayService(
            StartCapture,
            OpenLastCapture,
            ShowSettings,
            ShowHistory,
            ExitApplication);

        _captureCoordinator = new CaptureCoordinator(SettingsService);
        _captureCoordinator.Captured += OnCaptured;

        _notificationService = new NativeNotificationService(OnNotificationInvoked);
        var nativeNotificationsAvailable = _notificationService.Register();

        _hotkeyService = new HotkeyService(StartCapture);
        if (!_hotkeyService.Register())
        {
            _trayService.ShowWarning(
                "Не удалось зарегистрировать Ctrl + Shift + S. Возможно, сочетание занято другой программой.");
        }

        if (Settings.ShowNotifications && !nativeNotificationsAvailable)
        {
            _trayService.ShowWarning(
                "Системные уведомления недоступны. MeowShot будет использовать собственную кликабельную карточку.");
        }
    }

    public void StartCapture()
    {
        Dispatcher.Invoke(() => _captureCoordinator?.Start());
    }

    public void ShowSettings()
    {
        Dispatcher.Invoke(() =>
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow(SettingsService);
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    public void ShowHistory()
    {
        Dispatcher.Invoke(() => new HistoryWindow(SettingsService).Show());
    }

    public void OpenLastCapture()
    {
        Dispatcher.Invoke(() =>
        {
            if (_lastCapture is null)
            {
                _trayService?.ShowWarning("Сначала сделайте снимок экрана.");
                return;
            }

            new EditorWindow(_lastCapture).Show();
        });
    }

    private void OnCaptured(BitmapSource bitmap, CaptureAction captureAction)
    {
        _lastCapture = bitmap;
        ClipboardService.SetImage(bitmap);

        var requestedAction = ResolveCaptureAction(captureAction);
        string? savedPath = null;
        if ((Settings.AutoSave && requestedAction != CaptureAction.Save)
            || (captureAction == CaptureAction.Default
                && Settings.AfterCaptureBehavior == AfterCaptureBehavior.SaveAndNotify))
        {
            savedPath = ScreenshotStorageService.Save(bitmap, Settings.SaveDirectory);
        }

        if (Settings.HistoryEnabled)
        {
            ScreenshotStorageService.SaveToHistory(bitmap, Settings.HistoryLimit);
        }

        if (requestedAction == CaptureAction.Edit)
        {
            new EditorWindow(bitmap).Show();
            return;
        }

        if (requestedAction == CaptureAction.Save)
        {
            SaveCaptureAs(bitmap);
            return;
        }

        if (requestedAction == CaptureAction.CopyOnly || !Settings.ShowNotifications)
        {
            return;
        }

        var captureId = NotificationCacheService.Store(bitmap);
        if (_notificationService?.ShowCapture(captureId, bitmap.PixelWidth, bitmap.PixelHeight, savedPath) == true)
        {
            return;
        }

        new CaptureToastWindow(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            savedPath,
            () => new EditorWindow(bitmap).Show(),
            () => SaveCaptureAs(bitmap)).Show();
    }

    private CaptureAction ResolveCaptureAction(CaptureAction requestedAction)
    {
        if (requestedAction != CaptureAction.Default)
        {
            return requestedAction;
        }

        return Settings.AfterCaptureBehavior switch
        {
            AfterCaptureBehavior.CopyAndEdit => CaptureAction.Edit,
            AfterCaptureBehavior.CopyOnly => CaptureAction.CopyOnly,
            AfterCaptureBehavior.SaveAndNotify => CaptureAction.Default,
            _ => CaptureAction.Default
        };
    }

    private void OnNotificationInvoked(string action, string? captureId)
    {
        Dispatcher.Invoke(() =>
        {
            var bitmap = NotificationCacheService.TryLoad(captureId) ?? _lastCapture;
            if (bitmap is null)
            {
                _trayService?.ShowWarning("Снимок для этого уведомления уже удалён из временного кэша.");
                return;
            }

            _lastCapture = bitmap;
            if (string.Equals(action, "save", StringComparison.OrdinalIgnoreCase))
            {
                SaveCaptureAs(bitmap);
            }
            else
            {
                new EditorWindow(bitmap).Show();
            }
        });
    }

    private void SaveCaptureAs(BitmapSource bitmap)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить снимок MeowShot",
            Filter = "PNG без потерь (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"MeowShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"
        };
        if (dialog.ShowDialog() == true)
        {
            ScreenshotStorageService.SavePng(bitmap, dialog.FileName);
        }
    }

    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _captureCoordinator?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notificationService?.Dispose();
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
