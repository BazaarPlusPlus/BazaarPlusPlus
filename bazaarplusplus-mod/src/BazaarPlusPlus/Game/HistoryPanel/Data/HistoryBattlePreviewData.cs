#nullable enable
using System;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

internal sealed class HistoryBattlePreviewData
{
    public static readonly HistoryBattlePreviewData Empty = new(
        new BppItemBoard(
            BppItemBoardId.Historical,
            BppItemBoardType.Reference,
            Array.Empty<BppItemBoardCard>()
        ),
        string.Empty
    );

    public HistoryBattlePreviewData(BppItemBoard? board, string signature)
    {
        Signature = signature ?? string.Empty;
        Board =
            board
            ?? new BppItemBoard(
                BppItemBoardId.Historical,
                BppItemBoardType.Reference,
                Array.Empty<BppItemBoardCard>(),
                Signature
            );
    }

    public BppItemBoard Board { get; }

    public string Signature { get; }

    public bool HasRenderableCards => Board.Cards.Count > 0;
}
