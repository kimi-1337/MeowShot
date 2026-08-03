using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MeowShot.Interop;

namespace MeowShot.Services;

internal static class ScreenCaptureService
{
    internal static NativeRect GetVirtualScreenBounds() => new()
    {
        Left = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
        Top = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
        Right = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen)
            + NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
        Bottom = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen)
            + NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen)
    };

    internal static BitmapSource CaptureVirtualScreen(bool includeCursor)
    {
        var bounds = GetVirtualScreenBounds();
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmapHandle = NativeMethods.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
        if (screenDc == IntPtr.Zero || memoryDc == IntPtr.Zero || bitmapHandle == IntPtr.Zero)
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmapHandle);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            if (screenDc != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }

            throw new InvalidOperationException("Windows не удалось подготовить ресурсы для снимка экрана.");
        }

        var oldObject = NativeMethods.SelectObject(memoryDc, bitmapHandle);

        try
        {
            if (!NativeMethods.BitBlt(
                    memoryDc, 0, 0, bounds.Width, bounds.Height,
                    screenDc, bounds.Left, bounds.Top,
                    NativeMethods.SrcCopy | NativeMethods.CaptureBlt))
            {
                throw new InvalidOperationException("Windows не удалось получить изображение рабочего стола.");
            }

            if (includeCursor)
            {
                DrawCursor(memoryDc, bounds.Left, bounds.Top);
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.SelectObject(memoryDc, oldObject);
            NativeMethods.DeleteObject(bitmapHandle);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    internal static IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var monitors = new List<DisplayMonitor>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr monitor, IntPtr _, ref NativeRect _, IntPtr _) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (NativeMethods.GetMonitorInfo(monitor, ref info))
                {
                    monitors.Add(new DisplayMonitor(
                        monitor,
                        info.Monitor,
                        (info.Flags & NativeMethods.MonitorInfofPrimary) != 0));
                }

                return true;
            }, IntPtr.Zero);
        return monitors;
    }

    internal static IReadOnlyList<CapturableWindow> GetCapturableWindows()
    {
        var windows = new List<CapturableWindow>();
        var currentProcessId = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == currentProcessId)
            {
                return true;
            }

            if ((NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64()
                 & NativeMethods.WsExToolWindow) != 0)
            {
                return true;
            }

            if (NativeMethods.DwmGetWindowAttribute(
                    window, NativeMethods.DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            if (NativeMethods.DwmGetWindowAttribute(
                    window,
                    NativeMethods.DwmwaExtendedFrameBounds,
                    out NativeRect bounds,
                    Marshal.SizeOf<NativeRect>()) != 0
                || bounds.Width < 10 || bounds.Height < 10)
            {
                return true;
            }

            var buffer = new char[512];
            var length = NativeMethods.GetWindowText(window, buffer, buffer.Length);
            var title = length > 0 ? new string(buffer, 0, length) : "Окно";
            windows.Add(new CapturableWindow(window, bounds, title));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    internal static BitmapSource Crop(BitmapSource source, NativeRect absoluteBounds, NativeRect virtualBounds)
    {
        var left = Math.Max(absoluteBounds.Left, virtualBounds.Left);
        var top = Math.Max(absoluteBounds.Top, virtualBounds.Top);
        var right = Math.Min(absoluteBounds.Right, virtualBounds.Right);
        var bottom = Math.Min(absoluteBounds.Bottom, virtualBounds.Bottom);
        if (right <= left || bottom <= top)
        {
            throw new ArgumentOutOfRangeException(nameof(absoluteBounds), "Область снимка находится за пределами экрана.");
        }

        var crop = new Int32Rect(
            left - virtualBounds.Left,
            top - virtualBounds.Top,
            right - left,
            bottom - top);
        var result = new CroppedBitmap(source, crop);
        result.Freeze();
        return result;
    }

    private static void DrawCursor(IntPtr targetDc, int virtualLeft, int virtualTop)
    {
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!NativeMethods.GetCursorInfo(ref cursor)
            || (cursor.Flags & NativeMethods.CursorShowing) == 0
            || !NativeMethods.GetIconInfo(cursor.Cursor, out var iconInfo))
        {
            return;
        }

        try
        {
            NativeMethods.DrawIconEx(
                targetDc,
                cursor.ScreenPosition.X - virtualLeft - (int)iconInfo.HotspotX,
                cursor.ScreenPosition.Y - virtualTop - (int)iconInfo.HotspotY,
                cursor.Cursor,
                0,
                0,
                0,
                IntPtr.Zero,
                NativeMethods.DiNormal);
        }
        finally
        {
            if (iconInfo.ColorBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(iconInfo.ColorBitmap);
            }

            if (iconInfo.MaskBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(iconInfo.MaskBitmap);
            }
        }
    }
}
