#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class ChineseLocaleModeSettingsDockEntry : ISettingsDockEntry
{
    private readonly IBppEventBus _eventBus;
    private IBppConfig? _config;

    public ChineseLocaleModeSettingsDockEntry(IBppEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public int Order => BppSettingsDockOrder.ChineseLocaleMode;

    public BppSettingsDockDefinition Build(IBppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        return new(
            "ChineseLocaleMode",
            ResolveChineseLocaleModeLabel,
            _ => ChineseScriptConverter.ResolveModeStatus(ReadChineseLocaleMode()),
            IsChineseLocaleOverrideActive,
            CycleChineseLocaleMode,
            collapseAfterActivate: false
        );
    }

    private IBppConfig Config =>
        _config
        ?? throw new InvalidOperationException(
            "ChineseLocaleModeSettingsDockEntry.Build must be called at startup."
        );

    private static string ResolveChineseLocaleModeLabel(string languageCode)
    {
        return L.Resolve(new LocalizedTextSet("Chinese Locale", "中文模式"));
    }

    private BppChineseLocaleMode ReadChineseLocaleMode()
    {
        return Config.ChineseLocaleModeConfig?.Value ?? BppChineseLocaleMode.Mainland;
    }

    private void CycleChineseLocaleMode()
    {
        var config = Config.ChineseLocaleModeConfig;
        if (config != null)
            config.Value = ChineseScriptConverter.GetNextMode(config.Value);

        _eventBus.Publish(new ChineseLocaleModeChanged());
    }

    private bool IsChineseLocaleOverrideActive()
    {
        return ReadChineseLocaleMode() != BppChineseLocaleMode.Mainland;
    }
}
