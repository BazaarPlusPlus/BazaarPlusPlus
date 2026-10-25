#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.DetailedPreview;

public static class DetailedPreviewSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new(
        "Detailed Preview",
        "详细预览",
        "詳細預覽",
        "Detaillierte Vorschau",
        "Prévia detalhada",
        "상세 미리보기",
        "Anteprima dettagliata"
    );

    public static CyclingSettingsDockEntry<bool> Create(Action<bool>? onChanged = null) =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.DetailedPreview,
            "DetailedPreview",
            language => Labels.Resolve(language, L.CurrentMode),
            config => config.EnableDetailedPreviewConfig.Value,
            (config, enabled) => config.EnableDetailedPreviewConfig.Value = enabled,
            onChanged
        );
}
