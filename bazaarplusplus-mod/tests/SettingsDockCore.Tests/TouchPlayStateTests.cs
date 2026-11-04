using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The Mobile UI's rules over what the game plays, which both hosts' ports only read: the
/// shortcuts, the preview switches, the enchant preview and the upgrade preview, over every
/// combination of a fight, a replay and an open recap.
/// </summary>
public sealed class TouchPlayStateTests
{
    public static TheoryData<bool, bool, bool> States()
    {
        var states = new TheoryData<bool, bool, bool>();
        foreach (var combat in new[] { false, true })
        foreach (var replay in new[] { false, true })
        foreach (var recap in new[] { false, true })
            states.Add(combat, replay, recap);
        return states;
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Each_rule_follows_its_facts(bool combat, bool replay, bool recap)
    {
        var state = new TouchPlayState(combat, replay, recap);

        // A fight or a replay hides the shortcuts and keeps every panel closed.
        Assert.Equal(combat || replay, state.IsPlaying);
        Assert.Equal(!combat, state.ShowsPreviewSwitches);
        Assert.Equal(!combat && !replay, state.OffersEnchantments);
        // A recap's card still offers its enchantments, but never previews its upgrade.
        Assert.Equal(!combat && !replay && !recap, state.PreviewsUpgrade);
    }
}
