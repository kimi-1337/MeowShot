using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MeowShot.Windows;

public partial class CaptureToastWindow : Window
{
    private readonly Action _edit;
    private readonly Action _save;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(8) };

    public CaptureToastWindow(int width, int height, string? savedPath, Action edit, Action save)
    {
        InitializeComponent();
        _edit = edit;
        _save = save;
        DetailsText.Text = savedPath is null
            ? $"{width} × {height} · скопирован в буфер"
            : $"{width} × {height} · скопирован и сохранён";
        Loaded += OnLoaded;
        _timer.Tick += (_, _) => Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 16;
        Top = workArea.Bottom - ActualHeight - 16;
        _timer.Start();
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        InvokeAndClose(_edit);
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase)
            {
                return true;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void EditButton_Click(object sender, RoutedEventArgs e) => InvokeAndClose(_edit);
    private void SaveButton_Click(object sender, RoutedEventArgs e) => InvokeAndClose(_save);
    private void Window_MouseEnter(object sender, MouseEventArgs e) => _timer.Stop();
    private void Window_MouseLeave(object sender, MouseEventArgs e) => _timer.Start();

    private void InvokeAndClose(Action action)
    {
        _timer.Stop();
        Close();
        action();
    }
}
