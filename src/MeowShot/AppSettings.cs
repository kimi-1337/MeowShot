using System.IO;

namespace MeowShot;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = true;
    public bool AutoSave { get; set; }
    public string SaveDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MeowShot");
    public bool IncludeCursor { get; set; }
    public bool CaptureAllMonitorsInFullScreenMode { get; set; }
    public bool HistoryEnabled { get; set; }
    public int HistoryLimit { get; set; } = 20;
}
