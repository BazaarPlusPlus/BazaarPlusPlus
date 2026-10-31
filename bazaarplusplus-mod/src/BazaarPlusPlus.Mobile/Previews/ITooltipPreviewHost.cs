using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Previews;

/// <summary>
/// A host's read and write of the game's item tooltip for <see cref="TouchPreviewSwitches"/>. The
/// tooltip's types and members differ per host, so each host adapts them here (ADR-0025 decision
/// 5), with the upgrade and enchant previews themselves, which stay in the host. Main thread.
/// </summary>
internal interface ITooltipPreviewHost
{
    /// <summary>
    /// Whether an item tooltip that can carry the switches shows this frame: not during a fight,
    /// and not under a native dialog. <paramref name="card"/> changes with the inspected card, and
    /// a new one starts the switches again; <paramref name="render"/> changes with each render of
    /// the tooltip, and a new one draws them again.
    /// </summary>
    bool TryReadShown(out object card, out object render);

    /// <summary>The shown tooltip's description scroll, which the switches join; null until it exists.</summary>
    ScrollRect? DescriptionScroll();

    /// <summary>Whether the shown tooltip's item can switch its upgrade preview.</summary>
    bool CanToggleUpgrade { get; }

    /// <summary>Whether the shown tooltip shows its upgrade preview.</summary>
    bool UpgradeShown { get; }

    /// <summary>Turns the shown tooltip's upgrade preview on or off and renders the tooltip again.</summary>
    void ToggleUpgrade();

    /// <summary>
    /// Whether the shown tooltip offers enchantments behind its switch: its item has others, and
    /// its main text lists none.
    /// </summary>
    bool OffersEnchantments();

    /// <summary>Each offered enchantment's name and effect on the shown tooltip's item.</summary>
    IReadOnlyList<(string Label, string Text)> EnchantmentEffects();

    /// <summary>The switches let go of the tooltip: the host forgets it and ends the upgrade preview it holds.</summary>
    void Release(string reason);
}
