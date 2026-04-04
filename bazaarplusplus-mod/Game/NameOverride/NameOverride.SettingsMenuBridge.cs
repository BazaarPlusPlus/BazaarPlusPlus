#nullable enable

using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.NameOverride;

internal sealed class NameOverrideSettingsMenuBridge : SettingsMenuToggleBridge
{
    internal NameOverrideSettingsMenuBridge(
        Func<bool> readValue,
        Action<bool> writeValue,
        Action? refreshUi = null
    )
        : base(readValue, writeValue, _ => refreshUi?.Invoke()) { }
}
