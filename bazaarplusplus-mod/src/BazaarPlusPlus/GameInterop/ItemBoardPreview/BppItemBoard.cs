#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class BppItemBoard
{
    public static readonly BppItemBoard Empty = new(
        BppItemBoardId.Historical,
        BppItemBoardType.Reference,
        Array.Empty<BppItemBoardCard>(),
        string.Empty
    );

    public BppItemBoard(
        BppItemBoardId id,
        BppItemBoardType type,
        IReadOnlyList<BppItemBoardCard>? cards = null,
        string? signature = null
    )
    {
        Id = id;
        Type = type;
        Cards = cards?.Where(card => card != null).ToArray() ?? Array.Empty<BppItemBoardCard>();
        Signature = signature ?? string.Empty;
    }

    public BppItemBoardId Id { get; }

    public BppItemBoardType Type { get; }

    public string Signature { get; }

    public IReadOnlyList<BppItemBoardCard> Cards { get; }

    public bool HasCards => Cards.Count > 0;

    public BppItemBoard WithCards(
        IReadOnlyList<BppItemBoardCard> cards,
        string? signature = null
    ) => new(Id, Type, cards, signature ?? Signature);
}
