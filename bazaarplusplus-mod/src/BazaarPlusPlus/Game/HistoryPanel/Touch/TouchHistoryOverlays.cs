#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Touch;

/// <summary>One layer History draws over its sheet: a dialog, the card inspection or the skills sheet.</summary>
internal interface IHistoryOverlay
{
    bool IsOpen { get; }

    /// <summary>False when it was not open.</summary>
    bool Close(string reason);
}

/// <summary>An overlay that rents cards, which the rental destroys on every scene load.</summary>
internal interface IHistoryCardOverlay : IHistoryOverlay
{
    /// <summary>Every rented card goes back, open or not, without a log line.</summary>
    void ReturnCards();
}

/// <summary>
/// History's modal layer on Android (ADR-0022's modal layer): the note and More dialogs, which
/// hold no card, and the card inspection and skills sheet, which rent theirs. The back key closes
/// the top-most one in that order. A close of the panel, a relayout and a disposal close all of
/// them. A scene change closes only the card overlays, since the rental destroys every card right
/// after it: the dialogs live on the panel's DontDestroyOnLoad canvas, so a note keeps its unsaved
/// draft and More its typed code, as on the desktop, whose scene change rebuilds only the boards.
/// Unity-free, so <c>AndroidHost.Tests</c> drives it with fakes (ADR-0016).
/// </summary>
internal sealed class TouchHistoryOverlays
{
    private readonly IHistoryOverlay[] _dialogs;
    private readonly IHistoryCardOverlay[] _cardOverlays;

    /// <param name="dialogs">The overlays that hold no card, top-most first.</param>
    /// <param name="cardOverlays">The overlays that rent cards, top-most first, under the dialogs.</param>
    public TouchHistoryOverlays(
        IReadOnlyList<IHistoryOverlay> dialogs,
        IReadOnlyList<IHistoryCardOverlay> cardOverlays
    )
    {
        _dialogs = dialogs?.ToArray() ?? throw new ArgumentNullException(nameof(dialogs));
        _cardOverlays =
            cardOverlays?.ToArray() ?? throw new ArgumentNullException(nameof(cardOverlays));
    }

    /// <summary>True while any overlay is open: the back key then goes to <see cref="DismissTop"/>.</summary>
    public bool AnyOpen => _dialogs.Any(o => o.IsOpen) || _cardOverlays.Any(o => o.IsOpen);

    /// <summary>The back key: the top-most open overlay closes. False when none was open.</summary>
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

    /// <summary>The panel closes, lays itself out again or goes away: every overlay closes.</summary>
    public void CloseAll(string reason)
    {
        foreach (var overlay in _dialogs)
            overlay.Close(reason);
        CloseCardOverlays(reason);
    }

    /// <summary>
    /// A scene loaded: the card overlays close and return their cards; the dialogs stay as they
    /// are. Returns how many dialogs stayed open.
    /// </summary>
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
