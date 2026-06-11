#nullable enable
using System;

namespace BazaarPlusPlus.Game.Settings;

internal class SettingsMenuToggleBridge
{
    private readonly Func<bool> _readValue;
    private readonly Action<bool> _writeValue;
    private readonly Action<bool>? _onChanged;

    internal SettingsMenuToggleBridge(
        Func<bool> readValue,
        Action<bool> writeValue,
        Action<bool>? onChanged = null
    )
    {
        _readValue = readValue ?? throw new ArgumentNullException(nameof(readValue));
        _writeValue = writeValue ?? throw new ArgumentNullException(nameof(writeValue));
        _onChanged = onChanged;
    }

    internal bool GetInitialValue()
    {
        return _readValue();
    }

    internal void ApplyValue(bool value)
    {
        _writeValue(value);
        _onChanged?.Invoke(value);
    }
}
