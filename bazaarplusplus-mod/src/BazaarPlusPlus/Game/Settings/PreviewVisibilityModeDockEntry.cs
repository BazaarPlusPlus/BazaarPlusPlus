#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BepInEx.Configuration;

namespace BazaarPlusPlus.Game.Settings;

/// <summary>
/// Shared base for settings dock entries that toggle a feature's
/// <see cref="PreviewVisibilityMode"/>. Subclasses only supply the dock ordering,
/// key, label resolver, and which <see cref="IBppConfig"/> entry backs the mode;
/// the read/override/cycle behavior is identical across them.
/// </summary>
internal abstract class PreviewVisibilityModeDockEntry : ISettingsDockEntry
{
    public abstract int Order { get; }

    protected abstract string Key { get; }

    protected abstract Func<string, string> ResolveLabel { get; }

    protected abstract ConfigEntry<PreviewVisibilityMode>? GetModeConfig(IBppConfig config);

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            Key,
            ResolveLabel,
            languageCode =>
                BppSettingsDockCatalog.ResolvePreviewVisibilityModeStatus(
                    ReadMode(config),
                    languageCode
                ),
            () => IsOverrideActive(config),
            () => CycleMode(config),
            collapseAfterActivate: false
        );

    private PreviewVisibilityMode ReadMode(IBppConfig config) =>
        GetModeConfig(config)?.Value ?? BppConfig.DefaultEnchantPreviewMode;

    private bool IsOverrideActive(IBppConfig config) =>
        ReadMode(config) != PreviewVisibilityMode.Off;

    private void CycleMode(IBppConfig config)
    {
        var entry = GetModeConfig(config);
        if (entry != null)
            entry.Value = BppSettingsDockCatalog.NextPreviewVisibilityMode(entry.Value);
    }
}
