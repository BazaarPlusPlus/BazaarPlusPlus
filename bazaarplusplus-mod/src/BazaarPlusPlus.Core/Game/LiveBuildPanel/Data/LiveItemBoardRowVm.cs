#nullable enable

using BazaarPlusPlus.Core.Cards.ItemBoards;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

public sealed class LiveItemBoardRowVm
{
    public LiveItemBoardRowVm(BppItemBoard board, string title, string emptyText)
    {
        Board = board;
        Title = title;
        EmptyText = emptyText;
    }

    public BppItemBoard Board { get; }

    public string Title { get; }

    public string EmptyText { get; }

    public bool CanToggleCandidates => LiveBuildCandidateState.CanToggle(Board.Type);
}
