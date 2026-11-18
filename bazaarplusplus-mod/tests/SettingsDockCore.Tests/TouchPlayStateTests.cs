using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The Mobile UI's rules over what the game plays, which the host's port only reads: the
/// shortcuts, the preview switches, the enchant preview and the upgrade preview, over every
/// combination of a fight, a replay and an open recap.
/// </summary>
public sealed class TouchPlayStateTests
{
    // A fight or a replay hides the shortcuts and keeps every panel closed. A recap's card still
    // offers its enchantments, but never previews its upgrade.
    [Theory]
    [InlineData(false, false, false, false, true, true, true)]
    [InlineData(false, false, true, false, true, true, false)]
    [InlineData(false, true, false, true, true, false, false)]
    [InlineData(false, true, true, true, true, false, false)]
    [InlineData(true, false, false, true, false, false, false)]
    [InlineData(true, false, true, true, false, false, false)]
    [InlineData(true, true, false, true, false, false, false)]
    [InlineData(true, true, true, true, false, false, false)]
    public void Each_rule_follows_its_facts(
        bool combat,
        bool replay,
        bool recap,
        bool isPlaying,
        bool showsSwitches,
        bool offersEnchantments,
        bool previewsUpgrade
    )
    {
        var state = new TouchPlayState(combat, replay, recap);

        Assert.Equal(isPlaying, state.IsPlaying);
        Assert.Equal(showsSwitches, state.ShowsPreviewSwitches);
        Assert.Equal(offersEnchantments, state.OffersEnchantments);
        Assert.Equal(previewsUpgrade, state.PreviewsUpgrade);
    }
}
