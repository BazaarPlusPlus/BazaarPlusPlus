#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

internal static class HotkeyTutorialSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new("Hotkey Tutorial", "快捷键教程");
    private static readonly LocalizedTextSet OpenStatuses = new("OPEN", "打开");

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }

    internal static string ResolveOpenStatus(string languageCode)
    {
        return OpenStatuses.Resolve(languageCode, L.CurrentMode);
    }
}
