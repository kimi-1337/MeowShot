using System.Windows.Interop;
using MeowShot.Interop;

namespace MeowShot.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4D53;
    private readonly Action _activated;
    private HwndSource? _source;
    private bool _registered;

    public HotkeyService(Action activated)
    {
        _activated = activated;
    }

    public bool Register()
    {
        var parameters = new HwndSourceParameters("MeowShot.HotkeyWindow")
        {
            ParentWindow = NativeMethods.HwndMessage,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WindowProc);
        _registered = NativeMethods.RegisterHotKey(
            _source.Handle,
            HotkeyId,
            NativeMethods.ModControl | NativeMethods.ModShift | NativeMethods.ModNoRepeat,
            NativeMethods.VkS);
        return _registered;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _activated();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered && _source is not null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        }

        _source?.RemoveHook(WindowProc);
        _source?.Dispose();
        _source = null;
        _registered = false;
    }
}
