using System.IO;
using System.Windows.Media.Imaging;

namespace MeowShot.Services;

public static class ScreenshotStorageService
{
    public static string HistoryDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeowShot", "History");

    public static string Save(BitmapSource bitmap, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = CreateUniquePath(directory);
        SavePng(bitmap, path);
        return path;
    }

    public static void SaveToHistory(BitmapSource bitmap, int limit)
    {
        Directory.CreateDirectory(HistoryDirectory);
        SavePng(bitmap, CreateUniquePath(HistoryDirectory));

        var files = Directory.EnumerateFiles(HistoryDirectory, "MeowShot_*.png")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(Math.Clamp(limit, 5, 100))
            .ToArray();
        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // A locked history item is harmless; cleanup will retry after the next capture.
            }
        }
    }

    public static BitmapSource Load(string path, int? decodePixelWidth = null)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth.HasValue)
        {
            bitmap.DecodePixelWidth = decodePixelWidth.Value;
        }

        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static void SavePng(BitmapSource bitmap, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static string CreateUniquePath(string directory)
    {
        var stem = $"MeowShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}";
        var path = Path.Combine(directory, stem + ".png");
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{stem}_{suffix++}.png");
        }

        return path;
    }
}
