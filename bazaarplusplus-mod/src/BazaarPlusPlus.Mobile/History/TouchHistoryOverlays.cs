namespace BazaarPlusPlus.Mobile.History;

internal interface IHistoryOverlay
{
    bool IsOpen { get; }

    bool Close(string reason);
}

internal interface IHistoryCardOverlay : IHistoryOverlay
{
    void ReturnCards();
}

// Scene loads invalidate every rental. Dialogs hold no rentals and their persistent
// canvas lets drafts and link codes survive.
internal sealed class TouchHistoryOverlays
{
    private readonly IHistoryOverlay[] _dialogs;
    private readonly IHistoryCardOverlay[] _cardOverlays;

    // Pass each group top-most first, with dialogs stacked above card overlays.
    public TouchHistoryOverlays(
        IReadOnlyList<IHistoryOverlay> dialogs,
        IReadOnlyList<IHistoryCardOverlay> cardOverlays
    )
    {
        _dialogs = dialogs?.ToArray() ?? throw new ArgumentNullException(nameof(dialogs));
        _cardOverlays =
            cardOverlays?.ToArray() ?? throw new ArgumentNullException(nameof(cardOverlays));
    }

    public bool AnyOpen => _dialogs.Any(o => o.IsOpen) || _cardOverlays.Any(o => o.IsOpen);

    public bool DismissTop(string reason)
    {
        foreach (var overlay in _dialogs)
            if (overlay.Close(reason))
                return true;
        foreach (var overlay in _cardOverlays)
            if (overlay.Close(reason))
                return true;
        return false;
    }

    public void CloseAll(string reason)
    {
        foreach (var overlay in _dialogs)
            overlay.Close(reason);
        CloseCardOverlays(reason);
    }

    public int SceneChanged(string reason)
    {
        CloseCardOverlays(reason);
        return _dialogs.Count(overlay => overlay.IsOpen);
    }

    private void CloseCardOverlays(string reason)
    {
        foreach (var overlay in _cardOverlays)
        {
            overlay.Close(reason);
            overlay.ReturnCards();
        }
    }
}
