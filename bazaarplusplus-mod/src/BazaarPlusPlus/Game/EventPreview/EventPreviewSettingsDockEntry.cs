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
            config => config.EnableEventPreviewConfig?.Value ?? true,
            (config, enabled) =>
            {
                var entry = config.EnableEventPreviewConfig;
                if (entry != null)
                    entry.Value = enabled;
            }
        );
}
