#nullable enable

using BazaarPlusPlus.GameInterop.ItemBoardPreview;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

internal sealed class LiveItemBoardRowVm
{
    public LiveItemBoardRowVm(
        BppItemBoard board,
        string title,
        string emptyText,
        string? emptyTooltip = null
    )
    {
        Board = board;
        Title = title;
        EmptyText = emptyText;
        EmptyTooltip = emptyTooltip ?? emptyText;
    }

    public BppItemBoard Board { get; }

    public string Title { get; }

    public string EmptyText { get; }

    public string EmptyTooltip { get; }

    public bool CanToggleCandidates => LiveBuildCandidateState.CanToggle(Board.Type);
}
