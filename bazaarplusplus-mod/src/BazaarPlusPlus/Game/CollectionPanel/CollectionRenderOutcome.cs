#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Ui;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Complete presentable result of a Collection view-state transition. Null command results mean
// the command was a same-value no-op (no debounce cancel, no render, no scroll reset).
internal sealed class CollectionRenderOutcome
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
