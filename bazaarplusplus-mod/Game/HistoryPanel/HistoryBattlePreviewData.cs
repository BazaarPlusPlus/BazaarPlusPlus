#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.PreviewSurface;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryBattlePreviewData
{
    private static readonly PreviewBoardModel EmptyBoard = new PreviewBoardModel
    {
        ItemCards = new List<PreviewCardSpec>(),
        SkillCards = new List<PreviewCardSpec>(),
        Metadata = new Dictionary<string, string>(),
        Signature = string.Empty,
    };

    public HistoryBattlePreviewData(PreviewBoardModel playerBoard, PreviewBoardModel opponentBoard)
    {
        PlayerBoard = playerBoard ?? CloneBoard(EmptyBoard);
        OpponentBoard = opponentBoard ?? CloneBoard(EmptyBoard);
    }

    public PreviewBoardModel PlayerBoard { get; }

    public PreviewBoardModel OpponentBoard { get; }

    public bool HasRenderablePlayerBoard => CountRenderableCards(PlayerBoard) > 0;

    public bool HasRenderableOpponentBoard => CountRenderableCards(OpponentBoard) > 0;

    public bool HasRenderableCards => HasRenderablePlayerBoard || HasRenderableOpponentBoard;

    public HistoryBattlePreviewData PlayerOnly()
    {
        return new HistoryBattlePreviewData(CloneBoard(PlayerBoard), CloneBoard(EmptyBoard));
    }

    public HistoryBattlePreviewData OpponentOnly()
    {
        return new HistoryBattlePreviewData(CloneBoard(EmptyBoard), CloneBoard(OpponentBoard));
    }

    public HistoryBattlePreviewData PlayerHandOnly()
    {
        return new HistoryBattlePreviewData(CloneItemBoard(PlayerBoard), CloneBoard(EmptyBoard));
    }

    public HistoryBattlePreviewData OpponentHandOnly()
    {
        return new HistoryBattlePreviewData(CloneBoard(EmptyBoard), CloneItemBoard(OpponentBoard));
    }

    private static int CountRenderableCards(PreviewBoardModel board)
    {
        if (board == null)
            return 0;

        return (board.ItemCards?.Count ?? 0) + (board.SkillCards?.Count ?? 0);
    }

    private static PreviewBoardModel CloneBoard(PreviewBoardModel source)
    {
        return new PreviewBoardModel
        {
            ItemCards =
                source?.ItemCards != null
                    ? new List<PreviewCardSpec>(source.ItemCards)
                    : new List<PreviewCardSpec>(),
            SkillCards =
                source?.SkillCards != null
                    ? new List<PreviewCardSpec>(source.SkillCards)
                    : new List<PreviewCardSpec>(),
            Metadata =
                source?.Metadata != null
                    ? new Dictionary<string, string>(source.Metadata)
                    : new Dictionary<string, string>(),
            Signature = source?.Signature ?? string.Empty,
        };
    }

    private static PreviewBoardModel CloneItemBoard(PreviewBoardModel source)
    {
        return new PreviewBoardModel
        {
            ItemCards =
                source?.ItemCards != null
                    ? new List<PreviewCardSpec>(source.ItemCards)
                    : new List<PreviewCardSpec>(),
            SkillCards = new List<PreviewCardSpec>(),
            Metadata =
                source?.Metadata != null
                    ? new Dictionary<string, string>(source.Metadata)
                    : new Dictionary<string, string>(),
            Signature = source?.Signature ?? string.Empty,
        };
    }
}
