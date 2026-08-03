using MeowShot.Interop;

namespace MeowShot.Services;

internal sealed class CaptureSession
{
    private bool _finished;
    private CaptureMode _mode = CaptureMode.Rectangle;

    internal CaptureMode Mode => _mode;
    internal event Action<CaptureMode>? ModeChanged;
    internal event Action<NativeRect?>? Finished;

    internal void SetMode(CaptureMode mode)
    {
        if (_finished || _mode == mode)
        {
            return;
        }

        _mode = mode;
        ModeChanged?.Invoke(mode);
    }

    internal void Complete(NativeRect bounds)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Finished?.Invoke(bounds);
    }

    internal void Cancel()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Finished?.Invoke(null);
    }
}
