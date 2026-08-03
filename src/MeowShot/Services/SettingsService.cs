using System.IO;
using System.Text.Json;

namespace MeowShot.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeowShot", "settings.json");

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions)
                    ?? new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        Current.HistoryLimit = Math.Clamp(Current.HistoryLimit, 5, 100);
        if (string.IsNullOrWhiteSpace(Current.SaveDirectory))
        {
            Current.SaveDirectory = new AppSettings().SaveDirectory;
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(Current, JsonOptions));
    }
}
