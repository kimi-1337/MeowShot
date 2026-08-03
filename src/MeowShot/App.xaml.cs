using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using MeowShot.Services;
using MeowShot.Windows;

namespace MeowShot;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private HotkeyService? _hotkeyService;
    private TrayService? _trayService;
    private CaptureCoordinator? _captureCoordinator;
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

        _trayService = new TrayService(
            StartCapture,
            OpenLastCapture,
            ShowSettings,
            ShowHistory,
            ExitApplication);

        _captureCoordinator = new CaptureCoordinator(SettingsService);
        _captureCoordinator.Captured += OnCaptured;

        _hotkeyService = new HotkeyService(StartCapture);
        if (!_hotkeyService.Register())
        {
            _trayService.ShowWarning(
                "Не удалось зарегистрировать Ctrl + Shift + S. Возможно, сочетание занято другой программой.");
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

    private void OnCaptured(BitmapSource bitmap)
    {
        _lastCapture = bitmap;
        ClipboardService.SetImage(bitmap);

        string? savedPath = null;
        if (Settings.AutoSave)
        {
            savedPath = ScreenshotStorageService.Save(bitmap, Settings.SaveDirectory);
        }

        if (Settings.HistoryEnabled)
        {
            ScreenshotStorageService.SaveToHistory(bitmap, Settings.HistoryLimit);
        }

        var message = savedPath is null
            ? "Снимок скопирован в буфер обмена. Нажмите, чтобы открыть редактор."
            : $"Снимок скопирован и сохранён в {savedPath}. Нажмите, чтобы открыть редактор.";
        _trayService?.ShowCaptureNotification(message, OpenLastCapture);
    }

    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _captureCoordinator?.Dispose();
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        _singleInstanceMutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
