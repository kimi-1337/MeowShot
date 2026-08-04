using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MeowShot.Services;
using Microsoft.Win32;

namespace MeowShot.Windows;

public partial class EditorWindow : Window
{
    private const int MaxUndoStates = 12;
    private readonly List<byte[]> _undoStates = [];
    private readonly List<byte[]> _redoStates = [];
    private BitmapSource _baseBitmap;
    private EditorTool _tool = EditorTool.Pen;
    private Point _startPoint;
    private Shape? _activeShape;
    private bool _drawing;
    private bool _eraserChanged;
    private int _stepCounter = 1;
    private bool _updatingZoom;
    private bool _fitZoom = true;

    public EditorWindow(BitmapSource bitmap)
    {
        InitializeComponent();
        _baseBitmap = bitmap;
        SetBaseBitmap(bitmap);
        UpdateHistoryButtons();
        Loaded += (_, _) => Dispatcher.BeginInvoke(FitToWindow);
    }

    private void SetBaseBitmap(BitmapSource bitmap)
    {
        _baseBitmap = bitmap;
        ArtCanvas.Children.Clear();
        BaseImage.Source = bitmap;
        BaseImage.Width = bitmap.PixelWidth;
        BaseImage.Height = bitmap.PixelHeight;
        ArtCanvas.Width = bitmap.PixelWidth;
        ArtCanvas.Height = bitmap.PixelHeight;
        ArtCanvas.Children.Add(BaseImage);
        if (ImageSizeText is not null)
        {
            ImageSizeText.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} · PNG без потерь";
        }

        if (IsLoaded && _fitZoom)
        {
            Dispatcher.BeginInvoke(FitToWindow);
        }
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string value } && Enum.TryParse<EditorTool>(value, out var tool))
        {
            _tool = tool;
            if (StrokeLabel is not null)
            {
                StrokeLabel.Text = tool is EditorTool.Blur or EditorTool.Pixelate ? "Сила" : "Толщина";
            }
            if (ArtCanvas is not null)
            {
                ArtCanvas.Cursor = tool switch
                {
                    EditorTool.Text => Cursors.IBeam,
                    EditorTool.Step => Cursors.Hand,
                    EditorTool.Eraser => Cursors.Hand,
                    _ => Cursors.Cross
                };
            }
        }
    }

    private void ArtCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = Clamp(e.GetPosition(ArtCanvas));

        if (_tool == EditorTool.Text)
        {
            AddText(_startPoint);
            return;
        }

        if (_tool == EditorTool.Step)
        {
            AddStep(_startPoint);
            return;
        }

        if (_tool == EditorTool.Eraser)
        {
            _drawing = true;
            _eraserChanged = false;
            EraseAt(_startPoint);
            ArtCanvas.CaptureMouse();
            return;
        }

        PushUndoState();
        _drawing = true;
        ArtCanvas.CaptureMouse();
        _activeShape = CreateShape(_tool, _startPoint);
        if (_activeShape is not null)
        {
            ArtCanvas.Children.Add(_activeShape);
        }

        e.Handled = true;
    }

    private void ArtCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing)
        {
            return;
        }

        var point = Clamp(e.GetPosition(ArtCanvas));
        if (_tool == EditorTool.Eraser)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                EraseAt(point);
            }

            return;
        }

        if (_activeShape is null)
        {
            return;
        }

        UpdateShape(_activeShape, _startPoint, point);
    }

    private void ArtCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing)
        {
            return;
        }

        _drawing = false;
        ArtCanvas.ReleaseMouseCapture();

        if (_tool == EditorTool.Eraser)
        {
            _eraserChanged = false;
            return;
        }

        var end = Clamp(e.GetPosition(ArtCanvas));
        if (_activeShape is not null)
        {
            UpdateShape(_activeShape, _startPoint, end);
        }

        if (_tool == EditorTool.Crop)
        {
            ApplyCrop(_activeShape as Rectangle, _startPoint, end);
        }
        else if (_tool is EditorTool.Blur or EditorTool.Pixelate)
        {
            ApplyImageEffect(_activeShape as Rectangle, _startPoint, end);
        }

        _activeShape = null;
        e.Handled = true;
    }

    private Shape? CreateShape(EditorTool tool, Point start)
    {
        var color = SelectedColor();
        var thickness = StrokeWidthSlider.Value;
        return tool switch
        {
            EditorTool.Pen => new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Points = new PointCollection([start])
            },
            EditorTool.Brush => new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = Math.Max(10, thickness * 2.5),
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Points = new PointCollection([start])
            },
            EditorTool.Highlighter => new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B)),
                StrokeThickness = Math.Max(8, thickness * 3),
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Points = new PointCollection([start])
            },
            EditorTool.Line => NewLine(start, color, thickness),
            EditorTool.Arrow => new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Points = new PointCollection([start, start, start, start, start])
            },
            EditorTool.Rectangle => NewOutlinedRectangle(color, thickness),
            EditorTool.Ellipse => NewOutlinedEllipse(color, thickness),
            EditorTool.Fill => new Rectangle
            {
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1
            },
            EditorTool.Crop => new Rectangle
            {
                Stroke = Brushes.White,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection([5, 4]),
                Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
            },
            EditorTool.Blur or EditorTool.Pixelate => new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(124, 92, 252)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection([6, 4]),
                Fill = new SolidColorBrush(Color.FromArgb(34, 124, 92, 252))
            },
            _ => null
        };
    }

    private static Line NewLine(Point start, Color color, double thickness) => new()
    {
        X1 = start.X,
        Y1 = start.Y,
        X2 = start.X,
        Y2 = start.Y,
        Stroke = new SolidColorBrush(color),
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round
    };

    private static Rectangle NewOutlinedRectangle(Color color, double thickness) => new()
    {
        Stroke = new SolidColorBrush(color),
        StrokeThickness = thickness,
        Fill = Brushes.Transparent
    };

    private static Ellipse NewOutlinedEllipse(Color color, double thickness) => new()
    {
        Stroke = new SolidColorBrush(color),
        StrokeThickness = thickness,
        Fill = Brushes.Transparent
    };

    private void UpdateShape(Shape shape, Point start, Point end)
    {
        switch (shape)
        {
            case Polyline polyline when _tool is EditorTool.Pen or EditorTool.Brush or EditorTool.Highlighter:
                polyline.Points.Add(end);
                break;
            case Polyline arrow when _tool == EditorTool.Arrow:
                UpdateArrow(arrow, start, end);
                break;
            case Line line:
                line.X2 = end.X;
                line.Y2 = end.Y;
                break;
            case Rectangle or Ellipse:
                var rect = Normalize(start, end);
                Canvas.SetLeft(shape, rect.Left);
                Canvas.SetTop(shape, rect.Top);
                shape.Width = rect.Width;
                shape.Height = rect.Height;
                break;
        }
    }

    private static void UpdateArrow(Polyline arrow, Point start, Point end)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var length = Math.Min(28, Math.Max(12, Distance(start, end) * 0.25));
        const double wingAngle = Math.PI / 7;
        var firstWing = new Point(
            end.X - length * Math.Cos(angle - wingAngle),
            end.Y - length * Math.Sin(angle - wingAngle));
        var secondWing = new Point(
            end.X - length * Math.Cos(angle + wingAngle),
            end.Y - length * Math.Sin(angle + wingAngle));
        arrow.Points = new PointCollection([start, end, firstWing, end, secondWing]);
    }

    private void AddText(Point point)
    {
        var dialog = new TextInputDialog { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.EnteredText))
        {
            return;
        }

        PushUndoState();
        var text = new TextBlock
        {
            Text = dialog.EnteredText,
            Foreground = new SolidColorBrush(SelectedColor()),
            FontSize = Math.Max(16, StrokeWidthSlider.Value * 4),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Max(120, ArtCanvas.Width - point.X),
            IsHitTestVisible = true
        };
        Canvas.SetLeft(text, point.X);
        Canvas.SetTop(text, point.Y);
        ArtCanvas.Children.Add(text);
    }

    private void AddStep(Point point)
    {
        PushUndoState();
        var diameter = Math.Max(28, StrokeWidthSlider.Value * 4.5);
        var color = SelectedColor();
        var marker = new Grid
        {
            Width = diameter,
            Height = diameter,
            ToolTip = $"Шаг {_stepCounter}"
        };
        marker.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(color),
            Stroke = Brushes.White,
            StrokeThickness = Math.Max(2, diameter / 14)
        });
        marker.Children.Add(new TextBlock
        {
            Text = (_stepCounter++).ToString(),
            Foreground = GetReadableForeground(color),
            FontSize = diameter * 0.52,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Canvas.SetLeft(marker, Math.Clamp(point.X - diameter / 2, 0, Math.Max(0, ArtCanvas.Width - diameter)));
        Canvas.SetTop(marker, Math.Clamp(point.Y - diameter / 2, 0, Math.Max(0, ArtCanvas.Height - diameter)));
        ArtCanvas.Children.Add(marker);
    }

    private static Brush GetReadableForeground(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.62 ? Brushes.Black : Brushes.White;
    }

    private void EraseAt(Point point)
    {
        var hit = VisualTreeHelper.HitTest(ArtCanvas, point)?.VisualHit as DependencyObject;
        while (hit is not null && VisualTreeHelper.GetParent(hit) != ArtCanvas)
        {
            hit = VisualTreeHelper.GetParent(hit);
        }

        if (hit is not UIElement element || ReferenceEquals(element, BaseImage))
        {
            return;
        }

        if (!_eraserChanged)
        {
            PushUndoState();
            _eraserChanged = true;
        }

        ArtCanvas.Children.Remove(element);
    }

    private void ApplyCrop(Rectangle? cropShape, Point start, Point end)
    {
        if (cropShape is not null)
        {
            ArtCanvas.Children.Remove(cropShape);
        }

        var rect = Normalize(start, end);
        var crop = new Int32Rect(
            Math.Clamp((int)Math.Round(rect.Left), 0, _baseBitmap.PixelWidth - 1),
            Math.Clamp((int)Math.Round(rect.Top), 0, _baseBitmap.PixelHeight - 1),
            Math.Clamp((int)Math.Round(rect.Width), 1, _baseBitmap.PixelWidth),
            Math.Clamp((int)Math.Round(rect.Height), 1, _baseBitmap.PixelHeight));
        crop.Width = Math.Min(crop.Width, _baseBitmap.PixelWidth - crop.X);
        crop.Height = Math.Min(crop.Height, _baseBitmap.PixelHeight - crop.Y);

        if (rect.Width < 3 || rect.Height < 3)
        {
            RemoveLastUndoState();
            return;
        }

        var composite = RenderComposite();
        var cropped = new CroppedBitmap(composite, crop);
        cropped.Freeze();
        SetBaseBitmap(cropped);
    }

    private void ApplyImageEffect(Rectangle? preview, Point start, Point end)
    {
        if (preview is not null)
        {
            ArtCanvas.Children.Remove(preview);
        }

        var rect = Normalize(start, end);
        if (rect.Width < 3 || rect.Height < 3)
        {
            RemoveLastUndoState();
            return;
        }

        var composite = RenderComposite();
        var area = new Int32Rect(
            Math.Clamp((int)Math.Floor(rect.Left), 0, composite.PixelWidth - 1),
            Math.Clamp((int)Math.Floor(rect.Top), 0, composite.PixelHeight - 1),
            Math.Max(1, (int)Math.Ceiling(rect.Width)),
            Math.Max(1, (int)Math.Ceiling(rect.Height)));
        area.Width = Math.Min(area.Width, composite.PixelWidth - area.X);
        area.Height = Math.Min(area.Height, composite.PixelHeight - area.Y);

        var strength = (int)Math.Round(StrokeWidthSlider.Value);
        var result = _tool == EditorTool.Blur
            ? ImageEffectService.Blur(composite, area, Math.Max(3, strength))
            : ImageEffectService.Pixelate(composite, area, Math.Max(6, strength * 2));
        SetBaseBitmap(result);
    }

    private BitmapSource RenderComposite()
    {
        ArtCanvas.UpdateLayout();
        var render = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Round(ArtCanvas.Width)),
            Math.Max(1, (int)Math.Round(ArtCanvas.Height)),
            96, 96, PixelFormats.Pbgra32);
        render.Render(ArtCanvas);
        render.Freeze();
        return render;
    }

    private void PushUndoState()
    {
        _undoStates.Add(EncodePng(RenderComposite()));
        if (_undoStates.Count > MaxUndoStates)
        {
            _undoStates.RemoveAt(0);
        }

        _redoStates.Clear();
        UpdateHistoryButtons();
    }

    private void RemoveLastUndoState()
    {
        if (_undoStates.Count > 0)
        {
            _undoStates.RemoveAt(_undoStates.Count - 1);
        }
        UpdateHistoryButtons();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStates.Count == 0)
        {
            return;
        }

        _redoStates.Add(EncodePng(RenderComposite()));
        var index = _undoStates.Count - 1;
        RestoreState(_undoStates[index]);
        _undoStates.RemoveAt(index);
        UpdateHistoryButtons();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStates.Count == 0)
        {
            return;
        }

        _undoStates.Add(EncodePng(RenderComposite()));
        var index = _redoStates.Count - 1;
        RestoreState(_redoStates[index]);
        _redoStates.RemoveAt(index);
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        if (UndoButton is not null)
        {
            UndoButton.IsEnabled = _undoStates.Count > 0;
        }
        if (RedoButton is not null)
        {
            RedoButton.IsEnabled = _redoStates.Count > 0;
        }
    }

    private void RestoreState(byte[] bytes) => SetBaseBitmap(DecodePng(bytes));

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource DecodePng(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var result = decoder.Frames[0];
        result.Freeze();
        return result;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClipboardService.SetImage(RenderComposite());
            Title = "MeowShot — скопировано в буфер обмена";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "MeowShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить снимок",
            Filter = "PNG без потерь (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"MeowShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"
        };
        if (dialog.ShowDialog(this) == true)
        {
            ScreenshotStorageService.SavePng(RenderComposite(), dialog.FileName);
            Title = $"MeowShot — {System.IO.Path.GetFileName(dialog.FileName)}";
        }
    }

    private void ColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorPreview is not null)
        {
            ColorPreview.Background = new SolidColorBrush(SelectedColor());
        }
    }

    private void StrokeWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (StrokeValueText is not null)
        {
            StrokeValueText.Text = Math.Round(e.NewValue).ToString();
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingZoom || ArtworkScaleTransform is null)
        {
            return;
        }

        _fitZoom = false;
        ApplyZoom(e.NewValue);
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        _fitZoom = false;
        SetZoom(ZoomSlider.Value - 10);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        _fitZoom = false;
        SetZoom(ZoomSlider.Value + 10);
    }

    private void FitButton_Click(object sender, RoutedEventArgs e)
    {
        _fitZoom = true;
        FitToWindow();
    }

    private void ArtworkScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitZoom && IsLoaded)
        {
            Dispatcher.BeginInvoke(FitToWindow);
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        _fitZoom = false;
        SetZoom(ZoomSlider.Value + (e.Delta > 0 ? 10 : -10));
        e.Handled = true;
    }

    private void FitToWindow()
    {
        if (ArtworkScrollViewer is null || ArtCanvas.Width <= 0 || ArtCanvas.Height <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(1, ArtworkScrollViewer.ViewportWidth - 28);
        var availableHeight = Math.Max(1, ArtworkScrollViewer.ViewportHeight - 28);
        var scale = Math.Min(1.0, Math.Min(availableWidth / ArtCanvas.Width, availableHeight / ArtCanvas.Height));
        SetZoom(scale * 100);
    }

    private void SetZoom(double percent)
    {
        percent = Math.Clamp(percent, ZoomSlider.Minimum, ZoomSlider.Maximum);
        _updatingZoom = true;
        ZoomSlider.Value = percent;
        _updatingZoom = false;
        ApplyZoom(percent);
    }

    private void ApplyZoom(double percent)
    {
        var scale = Math.Clamp(percent, 10, 300) / 100;
        ArtworkScaleTransform.ScaleX = scale;
        ArtworkScaleTransform.ScaleY = scale;
        ZoomText.Text = $"{Math.Round(percent)}%";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Z when (Keyboard.Modifiers & ModifierKeys.Shift) != 0:
            case Key.Y:
                RedoButton_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Z:
                UndoButton_Click(sender, e);
                e.Handled = true;
                break;
            case Key.C:
                CopyButton_Click(sender, e);
                e.Handled = true;
                break;
            case Key.S:
                SaveButton_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    private Color SelectedColor()
    {
        var value = (ColorComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "#FFFF3B30";
        return (Color)ColorConverter.ConvertFromString(value);
    }

    private Point Clamp(Point point) => new(
        Math.Clamp(point.X, 0, ArtCanvas.Width),
        Math.Clamp(point.Y, 0, ArtCanvas.Height));

    private static Rect Normalize(Point start, Point end) => new(
        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
        Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));

    private static double Distance(Point first, Point second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
}
