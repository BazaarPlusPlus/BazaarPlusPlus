#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel;

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

    public CollectionRenderOutcome Render { get; }

    public CollectionPanelSelectionState Selection { get; }

    public Exception? HeroPreferenceFailure { get; }
}
