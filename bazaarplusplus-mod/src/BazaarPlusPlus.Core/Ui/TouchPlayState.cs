#nullable enable

namespace BazaarPlusPlus.Core.Ui;

/// <summary>
/// What the game plays, as a host reads it for the Mobile UI (the Mobile Platform Port's
/// <c>PlayState</c>): a fight, a replay, the recap of a past fight. The Mobile UI's rules over
/// those facts live here, apart from the host's reads. Unity-free.
/// </summary>
public readonly record struct TouchPlayState(bool InCombat, bool InReplay, bool RecapOpen)
{
    /// <summary>A fight or a replay plays: the shortcuts hide, no panel opens and no catalog warms.</summary>
    public bool IsPlaying => InCombat || InReplay;

    /// <summary>The item tooltip carries the touch preview switches: not during a fight.</summary>
    public bool ShowsPreviewSwitches => !InCombat;

    /// <summary>The enchant preview offers an item's other enchantments: no fight, no replay.</summary>
    public bool OffersEnchantments => !InCombat && !InReplay;

    /// <summary>An item may preview its upgrade: no fight, no replay, no recap.</summary>
    public bool PreviewsUpgrade => !InCombat && !InReplay && !RecapOpen;
}
