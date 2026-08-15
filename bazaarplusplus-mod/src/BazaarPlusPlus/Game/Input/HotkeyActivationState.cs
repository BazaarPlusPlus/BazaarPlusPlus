#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Input;

internal sealed class HotkeyActivationState
{
    private HotkeyActivationMode? _mode;
    private string? _bindingPath;
    private bool _toggled;
    private int _lastPressFrame = -1;

    internal bool Resolve(
        HotkeyActivationMode mode,
        string bindingPath,
        bool isHeld,
        bool wasPressedThisFrame,
        int frame
    )
    {
        if (_mode != mode || !string.Equals(_bindingPath, bindingPath, StringComparison.Ordinal))
        {
            _mode = mode;
            _bindingPath = bindingPath;
            _toggled = false;
            _lastPressFrame = -1;
        }

        if (mode == HotkeyActivationMode.Hold)
            return isHeld;

        if (wasPressedThisFrame && _lastPressFrame != frame)
        {
            _toggled = !_toggled;
            _lastPressFrame = frame;
        }

        return _toggled;
    }

    internal void Reset()
    {
        _mode = null;
        _bindingPath = null;
        _toggled = false;
        _lastPressFrame = -1;
    }
}
