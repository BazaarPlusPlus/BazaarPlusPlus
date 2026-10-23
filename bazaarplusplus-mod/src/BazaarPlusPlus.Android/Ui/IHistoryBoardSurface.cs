using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.PvpBattles;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The native boards in History's board rects: the seam #399's prefab adapter plugs into
/// without touching the coordinator or the layout. The panel hands it the rects' screen bounds
/// and the selected battle's snapshots; it reports each board's status, which Core's
/// <c>HistoryPanelDecisions.ArchiveStatus</c> turns into the board messages.
/// </summary>
internal interface IHistoryBoardSurface : IDisposable
{
    HistoryBoardFacts PlayerStatus { get; }

    HistoryBoardFacts OpponentStatus { get; }

    void SetPlayerBounds(Rect bounds);

    void SetOpponentBounds(Rect bounds);

    void Show(string? battleId, PvpBattleSnapshots? snapshots, bool showsBothBoards);

    void Hide();
}

/// <summary>
/// No native boards yet: nothing renders and no board reports a status, so the rects show only
/// the detail messages (loading, unavailable, download first).
/// </summary>
internal sealed class NoHistoryBoards : IHistoryBoardSurface
{
    public HistoryBoardFacts PlayerStatus => default;

    public HistoryBoardFacts OpponentStatus => default;

    public void SetPlayerBounds(Rect bounds) { }

    public void SetOpponentBounds(Rect bounds) { }

    public void Show(string? battleId, PvpBattleSnapshots? snapshots, bool showsBothBoards) { }

    public void Hide() { }

    public void Dispose() { }
}
