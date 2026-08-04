using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeowShot.Services;

public static class ImageEffectService
{
    public static BitmapSource Blur(BitmapSource source, Int32Rect area, int radius)
    {
        var (pixels, width, height, stride) = ReadBgra32(source);
        area = Clamp(area, width, height);
        if (area.Width < 1 || area.Height < 1)
        {
            return source;
        }

        radius = Math.Clamp(radius, 2, 40);
        var original = (byte[])pixels.Clone();
        var horizontal = (byte[])pixels.Clone();

        for (var y = area.Y; y < area.Y + area.Height; y++)
        {
            long blue = 0, green = 0, red = 0, alpha = 0;
            var windowRight = Math.Min(area.X + area.Width - 1, area.X + radius);
            for (var sampleX = area.X; sampleX <= windowRight; sampleX++)
            {
                Add(original, stride, sampleX, y, ref blue, ref green, ref red, ref alpha, 1);
            }

            for (var x = area.X; x < area.X + area.Width; x++)
            {
                var left = Math.Max(area.X, x - radius);
                var right = Math.Min(area.X + area.Width - 1, x + radius);
                WriteAverage(horizontal, stride, x, y, blue, green, red, alpha, right - left + 1);

                var outgoing = x - radius;
                var incoming = x + radius + 1;
                if (outgoing >= area.X)
                {
                    Add(original, stride, outgoing, y, ref blue, ref green, ref red, ref alpha, -1);
                }
                if (incoming < area.X + area.Width)
                {
                    Add(original, stride, incoming, y, ref blue, ref green, ref red, ref alpha, 1);
                }
            }
        }

        for (var x = area.X; x < area.X + area.Width; x++)
        {
            long blue = 0, green = 0, red = 0, alpha = 0;
            var windowBottom = Math.Min(area.Y + area.Height - 1, area.Y + radius);
            for (var sampleY = area.Y; sampleY <= windowBottom; sampleY++)
            {
                Add(horizontal, stride, x, sampleY, ref blue, ref green, ref red, ref alpha, 1);
            }

            for (var y = area.Y; y < area.Y + area.Height; y++)
            {
                var top = Math.Max(area.Y, y - radius);
                var bottom = Math.Min(area.Y + area.Height - 1, y + radius);
                WriteAverage(pixels, stride, x, y, blue, green, red, alpha, bottom - top + 1);

                var outgoing = y - radius;
                var incoming = y + radius + 1;
                if (outgoing >= area.Y)
                {
                    Add(horizontal, stride, x, outgoing, ref blue, ref green, ref red, ref alpha, -1);
                }
                if (incoming < area.Y + area.Height)
                {
                    Add(horizontal, stride, x, incoming, ref blue, ref green, ref red, ref alpha, 1);
                }
            }
        }

        return CreateBitmap(pixels, width, height, stride);
    }

    public static BitmapSource Pixelate(BitmapSource source, Int32Rect area, int blockSize)
    {
        var (pixels, width, height, stride) = ReadBgra32(source);
        area = Clamp(area, width, height);
        blockSize = Math.Clamp(blockSize, 4, 64);

        for (var top = area.Y; top < area.Y + area.Height; top += blockSize)
        {
            for (var left = area.X; left < area.X + area.Width; left += blockSize)
            {
                var right = Math.Min(area.X + area.Width, left + blockSize);
                var bottom = Math.Min(area.Y + area.Height, top + blockSize);
                long blue = 0, green = 0, red = 0, alpha = 0;
                var count = (right - left) * (bottom - top);
                for (var y = top; y < bottom; y++)
                {
                    for (var x = left; x < right; x++)
                    {
                        var index = y * stride + x * 4;
                        blue += pixels[index];
                        green += pixels[index + 1];
                        red += pixels[index + 2];
                        alpha += pixels[index + 3];
                    }
                }

                for (var y = top; y < bottom; y++)
                {
                    for (var x = left; x < right; x++)
                    {
                        var index = y * stride + x * 4;
                        pixels[index] = (byte)(blue / Math.Max(1, count));
                        pixels[index + 1] = (byte)(green / Math.Max(1, count));
                        pixels[index + 2] = (byte)(red / Math.Max(1, count));
                        pixels[index + 3] = (byte)(alpha / Math.Max(1, count));
                    }
                }
            }
        }

        return CreateBitmap(pixels, width, height, stride);
    }

    private static void Add(
        byte[] pixels,
        int stride,
        int x,
        int y,
        ref long blue,
        ref long green,
        ref long red,
        ref long alpha,
        int direction)
    {
        var index = y * stride + x * 4;
        blue += pixels[index] * direction;
        green += pixels[index + 1] * direction;
        red += pixels[index + 2] * direction;
        alpha += pixels[index + 3] * direction;
    }

    private static void WriteAverage(
        byte[] destination,
        int stride,
        int targetX,
        int targetY,
        long blue,
        long green,
        long red,
        long alpha,
        int count)
    {
        var target = targetY * stride + targetX * 4;
        destination[target] = (byte)(blue / Math.Max(1, count));
        destination[target + 1] = (byte)(green / Math.Max(1, count));
        destination[target + 2] = (byte)(red / Math.Max(1, count));
        destination[target + 3] = (byte)(alpha / Math.Max(1, count));
    }

    private static (byte[] Pixels, int Width, int Height, int Stride) ReadBgra32(BitmapSource source)
    {
        BitmapSource converted = source;
        if (source.Format != PixelFormats.Bgra32)
        {
            converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }

        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return (pixels, converted.PixelWidth, converted.PixelHeight, stride);
    }

    private static BitmapSource CreateBitmap(byte[] pixels, int width, int height, int stride)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static Int32Rect Clamp(Int32Rect area, int width, int height)
    {
        var x = Math.Clamp(area.X, 0, Math.Max(0, width - 1));
        var y = Math.Clamp(area.Y, 0, Math.Max(0, height - 1));
        return new Int32Rect(
            x,
            y,
            Math.Clamp(area.Width, 0, width - x),
            Math.Clamp(area.Height, 0, height - y));
    }
}
