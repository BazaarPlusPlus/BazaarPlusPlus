#nullable enable
using BazaarPlusPlus.GameInterop.Input;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private NativeTextInputLease? _inputLease;

    private void UpdateInputFocus()
    {
        if (!_visible || !IsTextInputFocused())
        {
            ReleaseInputFocus();
            return;
        }
        if (_inputLease?.IsCurrent == false)
            ReleaseInputFocus();
        _inputLease ??= NativeTextInputLease.TryAcquire();
    }

    private void ReleaseInputFocus()
    {
        _inputLease?.Dispose();
        _inputLease = null;
    }
}
