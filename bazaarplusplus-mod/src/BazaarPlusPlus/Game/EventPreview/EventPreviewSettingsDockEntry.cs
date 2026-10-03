#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EventPreviewSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.EventPreview,
            "EventPreview",
            EventPreviewSettingsMenuLabel.Resolve,
            config => config.EnableEventPreviewConfig.Value,
            (config, enabled) =>
            {
                config.EnableEventPreviewConfig.Value = enabled;
            }
        );
}
