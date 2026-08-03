using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MeowShot.Services;

public static class ClipboardService
{
    public static void SetImage(BitmapSource bitmap)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var png = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(png);
                png.Position = 0;

                var data = new DataObject();
                data.SetImage(bitmap);
                data.SetData("PNG", png, false);
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (ExternalException exception)
            {
                lastError = exception;
                Thread.Sleep(40 * (attempt + 1));
            }
        }

        throw new InvalidOperationException("Буфер обмена временно занят другой программой.", lastError);
    }
}
