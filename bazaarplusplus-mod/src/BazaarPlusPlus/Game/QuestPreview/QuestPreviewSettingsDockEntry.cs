#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.QuestPreview;

internal static class QuestPreviewSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create(Action clearPooledTooltips)
    {
        if (clearPooledTooltips == null)
            throw new ArgumentNullException(nameof(clearPooledTooltips));

        return CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.QuestPreview,
            "QuestPreview",
            QuestPreviewSettingsMenuLabel.Resolve,
            config => config.EnableQuestPreviewConfig?.Value ?? false,
            (config, enabled) =>
            {
                var entry = config.EnableQuestPreviewConfig;
                if (entry != null)
                    entry.Value = enabled;
            },
            enabled =>
            {
                if (enabled)
                    return;

                clearPooledTooltips();
            }
        );
    }
}
