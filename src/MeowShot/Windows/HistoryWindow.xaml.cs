using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MeowShot.Services;

namespace MeowShot.Windows;

public partial class HistoryWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly ObservableCollection<HistoryItem> _items = [];

    public HistoryWindow(SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        HistoryList.ItemsSource = _items;
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _items.Clear();
        if (!_settingsService.Current.HistoryEnabled)
        {
            DescriptionText.Text = "История выключена. Включить её можно в настройках.";
            return;
        }

        if (!Directory.Exists(ScreenshotStorageService.HistoryDirectory))
        {
            DescriptionText.Text = "История пока пуста.";
            return;
        }

        foreach (var file in Directory.EnumerateFiles(ScreenshotStorageService.HistoryDirectory, "*.png")
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.CreationTimeUtc))
        {
            try
            {
                var thumbnail = ScreenshotStorageService.Load(file.FullName, 360);
                _items.Add(new HistoryItem(
                    file.FullName,
                    file.Name,
                    $"{file.CreationTime:dd.MM.yyyy HH:mm} · {FormatSize(file.Length)}",
                    thumbnail));
            }
            catch
            {
                // Ignore a partially written or externally damaged file.
            }
        }

        DescriptionText.Text = _items.Count == 0
            ? "История пока пуста."
            : $"Локально сохранено снимков: {_items.Count}. Двойной щелчок открывает редактор.";
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenSelected();
    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (HistoryList.SelectedItem is not HistoryItem item)
        {
            return;
        }

        try
        {
            new EditorWindow(ScreenshotStorageService.Load(item.Path)).Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "MeowShot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryItem item)
        {
            return;
        }

        if (MessageBox.Show($"Удалить {item.DisplayName} из локальной истории?", "MeowShot",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(item.Path);
            _items.Remove(item);
            DescriptionText.Text = _items.Count == 0
                ? "История пока пуста."
                : $"Локально сохранено снимков: {_items.Count}. Двойной щелчок открывает редактор.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "MeowShot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_048_576 => $"{bytes / 1_048_576d:0.0} МБ",
        >= 1024 => $"{bytes / 1024d:0} КБ",
        _ => $"{bytes} Б"
    };
}

public sealed record HistoryItem(string Path, string DisplayName, string Details, BitmapSource Thumbnail);
