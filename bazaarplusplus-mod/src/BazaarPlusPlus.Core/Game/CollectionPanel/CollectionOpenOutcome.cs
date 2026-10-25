#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Result of CollectionViewState.Open: the open render, the resolved open selection (before any
// prune, for the hosts' open logs), and the hero-preference load failure when the store threw.
public sealed class CollectionOpenOutcome
{
    internal CollectionOpenOutcome(
        CollectionRenderOutcome render,
        CollectionPanelSelectionState selection,
        Exception? heroPreferenceFailure
    )
    {
        Render = render;
        Selection = selection;
        HeroPreferenceFailure = heroPreferenceFailure;
    }

    // Requests both scroll resets.
    public CollectionRenderOutcome Render { get; }

    public CollectionPanelSelectionState Selection { get; }

    // Non-null when the store's Load threw; the preference then counted as absent.
    public Exception? HeroPreferenceFailure { get; }
}
