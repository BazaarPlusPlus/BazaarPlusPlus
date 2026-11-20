#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Ui;

namespace BazaarPlusPlus.Game.CollectionPanel;

// A null command result is a no-op. Preserve pending search debounce and scroll position.
public sealed class CollectionRenderOutcome
{
    public CollectionRenderOutcome(
        CollectionPanelViewModel model,
        bool resetScroll,
        bool resetControlsScroll
    )
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        ResetScroll = resetScroll;
        ResetControlsScroll = resetControlsScroll;
    }

    public CollectionPanelViewModel Model { get; }

    public bool ResetScroll { get; }

    public bool ResetControlsScroll { get; }
}
