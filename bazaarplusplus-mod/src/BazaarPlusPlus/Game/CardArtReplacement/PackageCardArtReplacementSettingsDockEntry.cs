#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal sealed class PackageCardArtReplacementSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.PackageCardArtReplacement;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "PackageCardArtReplacement",
            PackageCardArtReplacementSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => PackageCardArtReplacementPolicy.IsEnabled(config),
                enabled => PackageCardArtReplacementPolicy.SetEnabled(config, enabled)
            )
        );
}
