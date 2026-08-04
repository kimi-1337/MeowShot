using System.Windows;
using System.Windows.Controls;
using MeowShot.Services;
using Forms = System.Windows.Forms;

namespace MeowShot;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;

    public MainWindow() : this(App.Current.SettingsService)
    {
    }

    public MainWindow(SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        LoadValues();
    }

    private void LoadValues()
    {
        var settings = _settingsService.Current;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        IncludeCursorCheckBox.IsChecked = settings.IncludeCursor;
        AllMonitorsCheckBox.IsChecked = settings.CaptureAllMonitorsInFullScreenMode;
        NotificationsCheckBox.IsChecked = settings.ShowNotifications;
        QuickActionsCheckBox.IsChecked = settings.ShowQuickActions;
        SelectAfterCaptureBehavior(settings.AfterCaptureBehavior);
        AutoSaveCheckBox.IsChecked = settings.AutoSave;
        SaveDirectoryTextBox.Text = settings.SaveDirectory;
        HistoryCheckBox.IsChecked = settings.HistoryEnabled;
        HistoryLimitTextBox.Text = settings.HistoryLimit.ToString();
        UpdateEnabledStates();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(HistoryLimitTextBox.Text, out var historyLimit) || historyLimit is < 5 or > 100)
        {
            MessageBox.Show("Количество снимков в истории должно быть от 5 до 100.", "MeowShot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var afterCaptureBehavior = SelectedAfterCaptureBehavior();
        if ((AutoSaveCheckBox.IsChecked == true || afterCaptureBehavior == AfterCaptureBehavior.SaveAndNotify)
            && string.IsNullOrWhiteSpace(SaveDirectoryTextBox.Text))
        {
            MessageBox.Show("Выберите папку автоматического сохранения.", "MeowShot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = _settingsService.Current;
        settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        settings.IncludeCursor = IncludeCursorCheckBox.IsChecked == true;
        settings.CaptureAllMonitorsInFullScreenMode = AllMonitorsCheckBox.IsChecked == true;
        settings.ShowNotifications = NotificationsCheckBox.IsChecked == true;
        settings.ShowQuickActions = QuickActionsCheckBox.IsChecked == true;
        settings.AfterCaptureBehavior = afterCaptureBehavior;
        settings.AutoSave = AutoSaveCheckBox.IsChecked == true;
        settings.SaveDirectory = SaveDirectoryTextBox.Text.Trim();
        settings.HistoryEnabled = HistoryCheckBox.IsChecked == true;
        settings.HistoryLimit = historyLimit;
        _settingsService.Save();

        if (!StartupService.Apply(settings.StartWithWindows))
        {
            MessageBox.Show("Настройки сохранены, но Windows не позволила изменить автозапуск.", "MeowShot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Close();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Папка для снимков MeowShot",
            UseDescriptionForTitle = true,
            SelectedPath = SaveDirectoryTextBox.Text,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            SaveDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void AutoSaveCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
    private void HistoryCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
    private void AfterCaptureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEnabledStates();

    private void UpdateEnabledStates()
    {
        if (SaveDirectoryTextBox is null || HistoryLimitPanel is null)
        {
            return;
        }

        var usesSaveDirectory = AutoSaveCheckBox.IsChecked == true
            || SelectedAfterCaptureBehavior() == AfterCaptureBehavior.SaveAndNotify;
        SaveDirectoryTextBox.IsEnabled = usesSaveDirectory;
        BrowseButton.IsEnabled = usesSaveDirectory;
        HistoryLimitPanel.IsEnabled = HistoryCheckBox.IsChecked == true;
    }

    private void SelectAfterCaptureBehavior(AfterCaptureBehavior behavior)
    {
        foreach (var item in AfterCaptureComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, behavior.ToString(), StringComparison.Ordinal))
            {
                AfterCaptureComboBox.SelectedItem = item;
                return;
            }
        }

        AfterCaptureComboBox.SelectedIndex = 0;
    }

    private AfterCaptureBehavior SelectedAfterCaptureBehavior()
    {
        var value = (AfterCaptureComboBox?.SelectedItem as ComboBoxItem)?.Tag as string;
        return Enum.TryParse<AfterCaptureBehavior>(value, out var behavior)
            ? behavior
            : AfterCaptureBehavior.CopyAndNotify;
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        App.Current.StartCapture();
        Close();
    }

    private void OpenHistoryButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowHistory();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
