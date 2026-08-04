using System.Windows.Media.Imaging;

namespace MeowShot.Services;

public static class NotificationCacheService
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeowShot", "NotificationCache");

    public static string Store(BitmapSource bitmap)
    {
        Directory.CreateDirectory(CacheDirectory);
        CleanupExpired();
        var id = Guid.NewGuid().ToString("N");
        ScreenshotStorageService.SavePng(bitmap, GetPath(id));
        return id;
    }

    public static BitmapSource? TryLoad(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(character => !char.IsAsciiHexDigit(character)))
        {
            return null;
        }

        var path = GetPath(id);
        try
        {
            return File.Exists(path) ? ScreenshotStorageService.Load(path) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void CleanupExpired()
    {
        if (!Directory.Exists(CacheDirectory))
        {
            return;
        }

        var threshold = DateTime.UtcNow.AddHours(-24);
        foreach (var path in Directory.EnumerateFiles(CacheDirectory, "*.png"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < threshold)
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Cache cleanup is best effort and retries at the next capture.
            }
        }
    }

    private static string GetPath(string id) => Path.Combine(CacheDirectory, $"{id}.png");
}
