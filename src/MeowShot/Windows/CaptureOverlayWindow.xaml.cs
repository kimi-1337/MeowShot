using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MeowShot.Interop;
using MeowShot.Services;

namespace MeowShot.Windows;

public partial class CaptureOverlayWindow : Window
{
    private readonly DisplayMonitor _monitor;
    private readonly IReadOnlyList<CapturableWindow> _windows;
    private readonly CaptureSession _session;
    private readonly NativeRect _virtualBounds;
    private readonly bool _captureAllMonitors;
    private readonly Rectangle[] _dimmers = new Rectangle[4];
    private readonly Border _selectionBorder;
    private Point _dragStart;
    private bool _dragging;
    private bool _closingSafely;
    private CapturableWindow? _hoveredWindow;

    internal bool IsPrimaryMonitor => _monitor.IsPrimary;

    internal CaptureOverlayWindow(
        DisplayMonitor monitor,
        BitmapSource monitorImage,
        IReadOnlyList<CapturableWindow> windows,
        CaptureSession session,
        NativeRect virtualBounds,
        bool captureAllMonitors)
    {
        InitializeComponent();
        _monitor = monitor;
        _windows = windows;
        _session = session;
        _virtualBounds = virtualBounds;
        _captureAllMonitors = captureAllMonitors;
        DesktopImage.Source = monitorImage;

        for (var index = 0; index < _dimmers.Length; index++)
        {
            _dimmers[index] = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)) };
            DimCanvas.Children.Add(_dimmers[index]);
        }

        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(124, 92, 252)),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        DimCanvas.Children.Add(_selectionBorder);

        _session.ModeChanged += Session_ModeChanged;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ShowSelection(new Rect(0, 0, 0, 0));
            UpdateModeUi(_session.Mode);
        };
        Closed += (_, _) => _session.ModeChanged -= Session_ModeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            _monitor.Bounds.Left,
            _monitor.Bounds.Top,
            _monitor.Bounds.Width,
            _monitor.Bounds.Height,
            NativeMethods.SwpShowWindow);
    }

    private void Session_ModeChanged(CaptureMode mode)
    {
        _dragging = false;
        _hoveredWindow = null;
        UpdateModeUi(mode);
        ClearSelection();
    }

    private void UpdateModeUi(CaptureMode mode)
    {
        RectangleButton.FontWeight = mode == CaptureMode.Rectangle ? FontWeights.Bold : FontWeights.Normal;
        WindowButton.FontWeight = mode == CaptureMode.Window ? FontWeights.Bold : FontWeights.Normal;
        ScreenButton.FontWeight = mode == CaptureMode.FullScreen ? FontWeights.Bold : FontWeights.Normal;
        Cursor = mode == CaptureMode.Rectangle ? Cursors.Cross : Cursors.Hand;
        HintText.Text = mode switch
        {
            CaptureMode.Rectangle => "Зажмите левую кнопку мыши и выделите область · Esc — отмена",
            CaptureMode.Window => "Наведите на окно и щёлкните · Esc — отмена",
            CaptureMode.FullScreen => _captureAllMonitors
                ? "Щёлкните, чтобы захватить все мониторы · Esc — отмена"
                : "Щёлкните, чтобы захватить этот монитор · Esc — отмена",
            _ => string.Empty
        };
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && Enum.TryParse<CaptureMode>(value, out var mode))
        {
            _session.SetMode(mode);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _session.Cancel();

    private void DimCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Activate();
        var point = e.GetPosition(DimCanvas);
        if (_session.Mode == CaptureMode.Rectangle)
        {
            _dragStart = point;
            _dragging = true;
            DimCanvas.CaptureMouse();
            ShowSelection(new Rect(point, point));
        }
        else if (_session.Mode == CaptureMode.Window)
        {
            var window = FindWindowAt(point);
            if (window is not null)
            {
                _session.Complete(window.Bounds);
            }
        }
        else
        {
            _session.Complete(_captureAllMonitors ? _virtualBounds : _monitor.Bounds);
        }

        e.Handled = true;
    }

    private void DimCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(DimCanvas);
        if (_session.Mode == CaptureMode.Rectangle && _dragging)
        {
            ShowSelection(Normalize(_dragStart, point));
        }
        else if (_session.Mode == CaptureMode.Window)
        {
            var window = FindWindowAt(point);
            if (window?.Handle != _hoveredWindow?.Handle)
            {
                _hoveredWindow = window;
                if (window is null)
                {
                    ClearSelection();
                }
                else
                {
                    ShowSelection(AbsoluteToLocal(window.Bounds));
                }
            }
        }
        else if (_session.Mode == CaptureMode.FullScreen)
        {
            ShowSelection(new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    private void DimCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_session.Mode != CaptureMode.Rectangle || !_dragging)
        {
            return;
        }

        _dragging = false;
        DimCanvas.ReleaseMouseCapture();
        var selection = Normalize(_dragStart, e.GetPosition(DimCanvas));
        if (selection.Width >= 3 && selection.Height >= 3)
        {
            _session.Complete(LocalToAbsolute(selection));
        }
        else
        {
            ClearSelection();
        }

        e.Handled = true;
    }

    private CapturableWindow? FindWindowAt(Point localPoint)
    {
        var screenPoint = LocalToAbsolute(localPoint);
        return _windows.FirstOrDefault(window =>
            screenPoint.X >= window.Bounds.Left && screenPoint.X < window.Bounds.Right
            && screenPoint.Y >= window.Bounds.Top && screenPoint.Y < window.Bounds.Bottom);
    }

    private void ShowSelection(Rect selection)
    {
        var clipped = Rect.Intersect(selection, new Rect(0, 0, ActualWidth, ActualHeight));
        if (clipped.IsEmpty || clipped.Width <= 0 || clipped.Height <= 0)
        {
            ClearSelection();
            return;
        }

        SetRect(_dimmers[0], new Rect(0, 0, ActualWidth, clipped.Top));
        SetRect(_dimmers[1], new Rect(0, clipped.Bottom, ActualWidth, Math.Max(0, ActualHeight - clipped.Bottom)));
        SetRect(_dimmers[2], new Rect(0, clipped.Top, clipped.Left, clipped.Height));
        SetRect(_dimmers[3], new Rect(clipped.Right, clipped.Top, Math.Max(0, ActualWidth - clipped.Right), clipped.Height));
        SetRect(_selectionBorder, clipped);
        _selectionBorder.Visibility = Visibility.Visible;
    }

    private void ClearSelection()
    {
        SetRect(_dimmers[0], new Rect(0, 0, ActualWidth, ActualHeight));
        for (var index = 1; index < _dimmers.Length; index++)
        {
            SetRect(_dimmers[index], Rect.Empty);
        }

        _selectionBorder.Visibility = Visibility.Collapsed;
    }

    private static void SetRect(FrameworkElement element, Rect rect)
    {
        Canvas.SetLeft(element, rect.IsEmpty ? 0 : rect.Left);
        Canvas.SetTop(element, rect.IsEmpty ? 0 : rect.Top);
        element.Width = rect.IsEmpty ? 0 : Math.Max(0, rect.Width);
        element.Height = rect.IsEmpty ? 0 : Math.Max(0, rect.Height);
    }

    private NativeRect LocalToAbsolute(Rect local)
    {
        var scaleX = _monitor.Bounds.Width / Math.Max(1.0, ActualWidth);
        var scaleY = _monitor.Bounds.Height / Math.Max(1.0, ActualHeight);
        return new NativeRect
        {
            Left = _monitor.Bounds.Left + (int)Math.Round(local.Left * scaleX),
            Top = _monitor.Bounds.Top + (int)Math.Round(local.Top * scaleY),
            Right = _monitor.Bounds.Left + (int)Math.Round(local.Right * scaleX),
            Bottom = _monitor.Bounds.Top + (int)Math.Round(local.Bottom * scaleY)
        };
    }

    private NativePoint LocalToAbsolute(Point local)
    {
        var scaleX = _monitor.Bounds.Width / Math.Max(1.0, ActualWidth);
        var scaleY = _monitor.Bounds.Height / Math.Max(1.0, ActualHeight);
        return new NativePoint
        {
            X = _monitor.Bounds.Left + (int)Math.Round(local.X * scaleX),
            Y = _monitor.Bounds.Top + (int)Math.Round(local.Y * scaleY)
        };
    }

    private Rect AbsoluteToLocal(NativeRect absolute)
    {
        var scaleX = Math.Max(1.0, ActualWidth) / _monitor.Bounds.Width;
        var scaleY = Math.Max(1.0, ActualHeight) / _monitor.Bounds.Height;
        return new Rect(
            (absolute.Left - _monitor.Bounds.Left) * scaleX,
            (absolute.Top - _monitor.Bounds.Top) * scaleY,
            absolute.Width * scaleX,
            absolute.Height * scaleY);
    }

    private static Rect Normalize(Point start, Point end) => new(
        Math.Min(start.X, end.X),
        Math.Min(start.Y, end.Y),
        Math.Abs(end.X - start.X),
        Math.Abs(end.Y - start.Y));

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _session.Cancel();
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _session.Cancel();
        e.Handled = true;
    }

    internal void CloseSafely()
    {
        _closingSafely = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_closingSafely)
        {
            e.Cancel = true;
            _session.Cancel();
            return;
        }

        base.OnClosing(e);
    }
}
