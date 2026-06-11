#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BepInEx.Configuration;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal sealed class ItemEnchantPreviewSettingsDockEntry : PreviewVisibilityModeDockEntry
{
    public override int Order => BppSettingsDockOrder.EnchantPreview;

    protected override string Key => "EnchantPreview";

    protected override Func<string, string> ResolveLabel => EnchantPreviewSettingsMenuLabel.Resolve;

    protected override ConfigEntry<PreviewVisibilityMode>? GetModeConfig(IBppConfig config) =>
        config.EnchantPreviewModeConfig;
}
