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
    private readonly System.Windows.Shapes.Path _dimLayer;
    private readonly Border _selectionBorder;
    private readonly Ellipse[] _handles = new Ellipse[8];
    private readonly Border _sizeBadge;
    private readonly TextBlock _sizeText;
    private Point _interactionStart;
    private Rect _interactionOriginal;
    private Rect _selection;
    private NativeRect? _confirmedBounds;
    private SelectionInteraction _interaction;
    private bool _dragging;
    private bool _selectionReady;
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

        _dimLayer = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(145, 0, 0, 0)),
            IsHitTestVisible = false
        };
        DimCanvas.Children.Add(_dimLayer);

        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(124, 92, 252)),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        DimCanvas.Children.Add(_selectionBorder);

        for (var index = 0; index < _handles.Length; index++)
        {
            _handles[index] = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(124, 92, 252)),
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            DimCanvas.Children.Add(_handles[index]);
        }

        _sizeText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        };
        _sizeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 28, 28, 32)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 4, 8, 4),
            Child = _sizeText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        DimCanvas.Children.Add(_sizeBadge);

        _session.ModeChanged += Session_ModeChanged;
        _session.QuickActionsChanged += Session_QuickActionsChanged;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ShowSelection(new Rect(0, 0, 0, 0));
            UpdateModeUi(_session.Mode);
            UpdateQuickActionsUi();
        };
        Closed += (_, _) =>
        {
            _session.ModeChanged -= Session_ModeChanged;
            _session.QuickActionsChanged -= Session_QuickActionsChanged;
        };
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
        _selectionReady = false;
        _interaction = SelectionInteraction.None;
        _hoveredWindow = null;
        UpdateModeUi(mode);
        ClearSelection();
    }

    private void Session_QuickActionsChanged(bool enabled)
    {
        UpdateQuickActionsUi();
        if (!enabled && _selectionReady)
        {
            CompleteSelection(CaptureAction.Default);
        }
    }

    private void UpdateQuickActionsUi()
    {
        QuickActionsToggleButton.Content = _session.ShowQuickActions
            ? "✓  С подтверждением"
            : "⚡  Сразу";
        QuickActionsToggleButton.Background = _session.ShowQuickActions
            ? new SolidColorBrush(Color.FromRgb(69, 64, 88))
            : new SolidColorBrush(Color.FromRgb(124, 92, 252));
        QuickActionsToggleButton.Foreground = Brushes.White;
    }

    private void UpdateModeUi(CaptureMode mode)
    {
        RectangleButton.FontWeight = mode == CaptureMode.Rectangle ? FontWeights.Bold : FontWeights.Normal;
        WindowButton.FontWeight = mode == CaptureMode.Window ? FontWeights.Bold : FontWeights.Normal;
        ScreenButton.FontWeight = mode == CaptureMode.FullScreen ? FontWeights.Bold : FontWeights.Normal;
        RectangleButton.Background = ModeBackground(mode == CaptureMode.Rectangle);
        WindowButton.Background = ModeBackground(mode == CaptureMode.Window);
        ScreenButton.Background = ModeBackground(mode == CaptureMode.FullScreen);
        Cursor = mode == CaptureMode.Rectangle ? Cursors.Cross : Cursors.Hand;
        HintText.Text = mode switch
        {
            CaptureMode.Rectangle => _selectionReady
                ? "Перемещайте область или тяните за маркеры · Enter — готово · Esc — заново"
                : "Зажмите левую кнопку мыши и выделите область · Esc — отмена",
            CaptureMode.Window => "Наведите на окно и щёлкните · Esc — отмена",
            CaptureMode.FullScreen => _captureAllMonitors
                ? "Щёлкните, чтобы захватить все мониторы · Esc — отмена"
                : "Щёлкните, чтобы захватить этот монитор · Esc — отмена",
            _ => string.Empty
        };
    }

    private static Brush ModeBackground(bool selected) => new SolidColorBrush(selected
        ? Color.FromRgb(124, 92, 252)
        : Color.FromRgb(57, 54, 67));

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && Enum.TryParse<CaptureMode>(value, out var mode))
        {
            _session.SetMode(mode);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _session.Cancel();

    private void QuickActionsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _session.SetQuickActions(!_session.ShowQuickActions);
        e.Handled = true;
    }

    private void DimCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Activate();
        var point = e.GetPosition(DimCanvas);
        if (_session.Mode == CaptureMode.Rectangle)
        {
            if (_selectionReady && e.ClickCount >= 2 && _selection.Contains(point))
            {
                CompleteSelection(CaptureAction.Default);
                e.Handled = true;
                return;
            }

            _interactionStart = point;
            _interactionOriginal = _selection;
            _interaction = _selectionReady ? HitTestInteraction(point) : SelectionInteraction.Creating;
            if (_interaction == SelectionInteraction.None)
            {
                _interaction = SelectionInteraction.Creating;
                _selectionReady = false;
            }

            _dragging = true;
            DimCanvas.CaptureMouse();
            QuickActionsPanel.Visibility = Visibility.Collapsed;
            if (_interaction == SelectionInteraction.Creating)
            {
                _selection = new Rect(point, point);
                HideSelectionVisuals();
            }
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
            _selection = UpdateSelection(point);
            ShowSelection(_selection);
        }
        else if (_session.Mode == CaptureMode.Rectangle && _selectionReady)
        {
            Cursor = CursorFor(HitTestInteraction(point));
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
        _selection = UpdateSelection(e.GetPosition(DimCanvas));
        if (_selection.Width >= 3 && _selection.Height >= 3)
        {
            _selectionReady = true;
            _interaction = SelectionInteraction.None;
            _confirmedBounds = LocalToAbsolute(_selection);
            ShowSelection(_selection);
            UpdateModeUi(CaptureMode.Rectangle);
            if (_session.ShowQuickActions)
            {
                QuickActionsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                CompleteSelection(CaptureAction.Default);
            }
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
            HideSelectionVisuals();
            return;
        }

        _dimLayer.Data = CreateDimGeometry(clipped);
        SetRect(_selectionBorder, clipped);
        _selectionBorder.Visibility = Visibility.Visible;
        UpdateSelectionAdornments(clipped);
    }

    private void ClearSelection()
    {
        HideSelectionVisuals();
        _selection = Rect.Empty;
        _confirmedBounds = null;
        _selectionReady = false;
        _interaction = SelectionInteraction.None;
        if (_session.Mode == CaptureMode.Rectangle)
        {
            Cursor = Cursors.Cross;
        }
    }

    private void HideSelectionVisuals()
    {
        _dimLayer.Data = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));

        _selectionBorder.Visibility = Visibility.Collapsed;
        _sizeBadge.Visibility = Visibility.Collapsed;
        QuickActionsPanel.Visibility = Visibility.Collapsed;
        foreach (var handle in _handles)
        {
            handle.Visibility = Visibility.Collapsed;
        }
    }

    private static void SetRect(FrameworkElement element, Rect rect)
    {
        Canvas.SetLeft(element, rect.IsEmpty ? 0 : rect.Left);
        Canvas.SetTop(element, rect.IsEmpty ? 0 : rect.Top);
        element.Width = rect.IsEmpty ? 0 : Math.Max(0, rect.Width);
        element.Height = rect.IsEmpty ? 0 : Math.Max(0, rect.Height);
    }

    private Geometry CreateDimGeometry(Rect selection)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        geometry.Children.Add(new RectangleGeometry(selection));
        return geometry;
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

    private Rect UpdateSelection(Point point)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (_interaction == SelectionInteraction.Creating)
        {
            return Rect.Intersect(Normalize(_interactionStart, Clamp(point, bounds)), bounds);
        }

        if (_interaction == SelectionInteraction.Moving)
        {
            var delta = point - _interactionStart;
            var left = Math.Clamp(_interactionOriginal.Left + delta.X, 0, Math.Max(0, ActualWidth - _interactionOriginal.Width));
            var top = Math.Clamp(_interactionOriginal.Top + delta.Y, 0, Math.Max(0, ActualHeight - _interactionOriginal.Height));
            return new Rect(left, top, _interactionOriginal.Width, _interactionOriginal.Height);
        }

        var leftEdge = _interactionOriginal.Left;
        var topEdge = _interactionOriginal.Top;
        var rightEdge = _interactionOriginal.Right;
        var bottomEdge = _interactionOriginal.Bottom;
        var clamped = Clamp(point, bounds);

        if (_interaction is SelectionInteraction.TopLeft or SelectionInteraction.Left or SelectionInteraction.BottomLeft)
        {
            leftEdge = Math.Min(clamped.X, rightEdge - 3);
        }
        if (_interaction is SelectionInteraction.TopRight or SelectionInteraction.Right or SelectionInteraction.BottomRight)
        {
            rightEdge = Math.Max(clamped.X, leftEdge + 3);
        }
        if (_interaction is SelectionInteraction.TopLeft or SelectionInteraction.Top or SelectionInteraction.TopRight)
        {
            topEdge = Math.Min(clamped.Y, bottomEdge - 3);
        }
        if (_interaction is SelectionInteraction.BottomLeft or SelectionInteraction.Bottom or SelectionInteraction.BottomRight)
        {
            bottomEdge = Math.Max(clamped.Y, topEdge + 3);
        }

        return new Rect(new Point(leftEdge, topEdge), new Point(rightEdge, bottomEdge));
    }

    private SelectionInteraction HitTestInteraction(Point point)
    {
        const double tolerance = 14;
        var positions = GetHandlePositions(_selection);
        for (var index = 0; index < positions.Length; index++)
        {
            if ((positions[index] - point).Length <= tolerance)
            {
                return (SelectionInteraction)(index + 2);
            }
        }

        return _selection.Contains(point) ? SelectionInteraction.Moving : SelectionInteraction.None;
    }

    private static Cursor CursorFor(SelectionInteraction interaction) => interaction switch
    {
        SelectionInteraction.TopLeft or SelectionInteraction.BottomRight => Cursors.SizeNWSE,
        SelectionInteraction.TopRight or SelectionInteraction.BottomLeft => Cursors.SizeNESW,
        SelectionInteraction.Top or SelectionInteraction.Bottom => Cursors.SizeNS,
        SelectionInteraction.Left or SelectionInteraction.Right => Cursors.SizeWE,
        SelectionInteraction.Moving => Cursors.SizeAll,
        _ => Cursors.Cross
    };

    private void UpdateSelectionAdornments(Rect selection)
    {
        var absolute = LocalToAbsolute(selection);
        _sizeText.Text = $"{absolute.Width} × {absolute.Height}";
        _sizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_sizeBadge, Math.Clamp(selection.Left, 4, Math.Max(4, ActualWidth - _sizeBadge.DesiredSize.Width - 4)));
        Canvas.SetTop(_sizeBadge, selection.Top >= 38 ? selection.Top - 34 : selection.Bottom + 6);
        _sizeBadge.Visibility = Visibility.Visible;

        var positions = GetHandlePositions(selection);
        for (var index = 0; index < _handles.Length; index++)
        {
            Canvas.SetLeft(_handles[index], positions[index].X - 5);
            Canvas.SetTop(_handles[index], positions[index].Y - 5);
            _handles[index].Visibility = _selectionReady ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static Point[] GetHandlePositions(Rect rect) =>
    [
        rect.TopLeft,
        new Point(rect.Left + rect.Width / 2, rect.Top),
        rect.TopRight,
        new Point(rect.Right, rect.Top + rect.Height / 2),
        rect.BottomRight,
        new Point(rect.Left + rect.Width / 2, rect.Bottom),
        rect.BottomLeft,
        new Point(rect.Left, rect.Top + rect.Height / 2)
    ];

    private static Point Clamp(Point point, Rect bounds) => new(
        Math.Clamp(point.X, bounds.Left, bounds.Right),
        Math.Clamp(point.Y, bounds.Top, bounds.Bottom));

    private void CompleteSelection(CaptureAction action)
    {
        if (_selectionReady && _confirmedBounds is { Width: >= 3, Height: >= 3 } bounds)
        {
            _session.Complete(bounds, action);
        }
    }

    private void CompleteButton_Click(object sender, RoutedEventArgs e) => CompleteSelection(CaptureAction.Default);
    private void EditButton_Click(object sender, RoutedEventArgs e) => CompleteSelection(CaptureAction.Edit);
    private void SaveButton_Click(object sender, RoutedEventArgs e) => CompleteSelection(CaptureAction.Save);
    private void ResetSelectionButton_Click(object sender, RoutedEventArgs e) => ClearSelection();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_selectionReady)
            {
                ClearSelection();
                UpdateModeUi(CaptureMode.Rectangle);
            }
            else
            {
                _session.Cancel();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _selectionReady)
        {
            CompleteSelection(CaptureAction.Default);
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectionReady)
        {
            ClearSelection();
        }
        else
        {
            _session.Cancel();
        }
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

    private enum SelectionInteraction
    {
        None = 0,
        Moving = 1,
        TopLeft = 2,
        Top = 3,
        TopRight = 4,
        Right = 5,
        BottomRight = 6,
        Bottom = 7,
        BottomLeft = 8,
        Left = 9,
        Creating = 10
    }
}
