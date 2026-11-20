using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Previews;

// Main thread only.
internal interface ITooltipPreviewHost
{
    // Card changes reset switch state. Render changes rebuild the UI without resetting it.
    bool TryReadShown(out object card, out object render);

    ScrollRect? DescriptionScroll();

    // The host supplies native eligibility. The Mobile UI applies play-state restrictions.
    bool CanToggleUpgrade { get; }

    bool UpgradeShown { get; }

    void ToggleUpgrade();

    bool OffersEnchantments();

    IReadOnlyList<(string Label, string Text)> EnchantmentEffects();

    void Release(string reason);
}
