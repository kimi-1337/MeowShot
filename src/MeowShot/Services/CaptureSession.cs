using MeowShot.Interop;

namespace MeowShot.Services;

internal sealed class CaptureSession
{
    private bool _finished;
    private CaptureMode _mode = CaptureMode.Rectangle;

    internal CaptureSession(bool showQuickActions)
    {
        ShowQuickActions = showQuickActions;
    }

    internal CaptureMode Mode => _mode;
    internal bool ShowQuickActions { get; private set; }
    internal event Action<CaptureMode>? ModeChanged;
    internal event Action<bool>? QuickActionsChanged;
    internal event Action<NativeRect?, CaptureAction>? Finished;

    internal void SetMode(CaptureMode mode)
    {
        if (_finished || _mode == mode)
        {
            return;
        }

        _mode = mode;
        ModeChanged?.Invoke(mode);
    }

    internal void SetQuickActions(bool enabled)
    {
        if (_finished || ShowQuickActions == enabled)
        {
            return;
        }

        ShowQuickActions = enabled;
        QuickActionsChanged?.Invoke(enabled);
    }

    internal void Complete(NativeRect bounds, CaptureAction action = CaptureAction.Default)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Finished?.Invoke(bounds, action);
    }

    internal void Cancel()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Finished?.Invoke(null, CaptureAction.Default);
    }
}
