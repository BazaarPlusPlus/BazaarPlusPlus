#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppSettingsDockPlacement
{
    internal const float DefaultSiblingGap = 8f;

    private BppSettingsDockPlacement(string key, SettingsNativeButtonId buttonId)
    {
        Key = key;
        ButtonId = buttonId;
    }

    internal string Key { get; }
    internal SettingsNativeButtonId ButtonId { get; }

    internal string DockButtonObjectName => $"BPP_CollectionDockButton_{Key}";

    internal static BppSettingsDockPlacement ForButton(
        string key,
        SettingsNativeButtonId buttonId
    ) => new(key, buttonId);
}
