using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatStatusBarSettingsMenuBridge : SettingsMenuToggleBridge
{
    internal CombatStatusBarSettingsMenuBridge(Func<bool> readValue, Action<bool> writeValue)
        : base(readValue, writeValue) { }
}
